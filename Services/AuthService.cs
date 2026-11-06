using System.Security.Cryptography;
using KalyanaVaran.Api.Common;
using KalyanaVaran.Api.Contracts;
using KalyanaVaran.Api.Data;
using KalyanaVaran.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace KalyanaVaran.Api.Services;

public sealed class AuthService(AppDbContext db, OtpService otp, TokenService tokens, ILogger<AuthService> logger)
{
    // A1
    public async Task<OtpSentResponse> RegisterAsync(RegisterRequest req, HttpContext http, CancellationToken ct)
    {
        var mobile = req.Mobile!.Trim();
        var email = req.Email!.Trim().ToLowerInvariant();

        var existing = await db.Users.Where(u => u.Mobile == mobile || u.Email == email).ToListAsync(ct);

        var activeByMobile = existing.FirstOrDefault(u => u.Mobile == mobile && u.Status != UserStatus.Pending);
        if (activeByMobile is not null)
            throw ApiException.Conflict("This mobile number is already registered. Please log in.", "mobile");

        var activeByEmail = existing.FirstOrDefault(u => u.Email == email && u.Status != UserStatus.Pending);
        if (activeByEmail is not null)
            throw ApiException.Conflict("This email is already registered. Please log in.", "email");

        // Abandoned registrations (never verified): reuse the one with this mobile (keeps its OTP
        // history so rate limits still apply) and drop a different pending row holding this email.
        var user = existing.FirstOrDefault(u => u.Mobile == mobile);
        var staleByEmail = existing.Where(u => u != user).ToList();
        if (staleByEmail.Count > 0)
        {
            db.Users.RemoveRange(staleByEmail);
            await db.SaveChangesAsync(ct);
        }

        var now = DateTime.UtcNow;
        if (user is null)
        {
            user = new User { MemberId = await NewMemberIdAsync(ct), Mobile = mobile, CreatedAt = now };
            db.Users.Add(user);
        }
        user.ProfileFor = req.ProfileFor!;
        user.FullName = req.FullName!.Trim();
        user.Gender = req.Gender!;
        user.Email = email;
        user.Status = UserStatus.Pending;
        user.TermsAcceptedAt = now;
        user.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        // The OTP screen shows "Sent via SMS & email": send both codes.
        // SMS is required (a failure is returned to the app); email is best-effort.
        var sent = await otp.SendAsync(user, "sms", "register", http, ct);
        try
        {
            await otp.SendAsync(user, OtpService.Email, "register", http, ct);
        }
        catch (ApiException ex) when (ex.Code is ErrorCodes.OtpRateLimited or ErrorCodes.OtpDeliveryFailed)
        {
            // SMS already went out; the user can still verify with the mobile OTP and
            // resend the email code later with POST /v1/auth/otp/resend { channel: "email" }.
            logger.LogWarning("Register {MemberId}: email OTP not sent ({Code}); continuing with SMS OTP.", user.MemberId, ex.Code);
        }
        return sent;
    }

    // A2
    public async Task<OtpSentResponse> SendOtpAsync(SendOtpRequest req, HttpContext http, CancellationToken ct)
    {
        var mobile = req.Mobile!.Trim();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Mobile == mobile, ct)
                   ?? throw ApiException.NotFound("No account found for this mobile number. Please register first.");
        if (user.Status == UserStatus.Blocked)
            throw ApiException.Forbidden("This account has been blocked. Please contact support.");

        var purpose = user.Status == UserStatus.Pending ? "register" : "login";
        return await otp.SendAsync(user, req.Channel, purpose, http, ct);
    }

    // A3
    public async Task<AuthSession> VerifyOtpAsync(VerifyOtpRequest req, HttpContext http, CancellationToken ct)
    {
        var result = await otp.VerifyAsync(req.OtpRequestId!, req.Mobile!.Trim(), req.Otp!, req.EmailOtp, ct);

        var user = await db.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.Id == result.UserId, ct)
                   ?? throw ApiException.NotFound("Account not found.");
        if (user.Status == UserStatus.Blocked)
            throw ApiException.Forbidden("This account has been blocked. Please contact support.");

        var now = DateTime.UtcNow;
        var isNewUser = user.Status == UserStatus.Pending;
        if (isNewUser)
        {
            user.Status = UserStatus.Active;
            user.MobileVerified = true;
        }
        if (result.EmailVerified) user.EmailVerified = true;
        if (user.Profile is null)
        {
            var (first, last) = ProfileParsing.SplitName(user.FullName);
            db.Profiles.Add(new Profile { UserId = user.Id, FirstName = first, LastName = last, UpdatedAt = now });
        }
        user.LastLoginAt = now;
        user.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        var authTokens = await tokens.IssueAsync(user, http, ct);
        return new AuthSession(authTokens, ToAuthUser(user, isNewUser));
    }

    // A5
    public Task<AuthTokens> RefreshAsync(RefreshRequest req, HttpContext http, CancellationToken ct) =>
        tokens.RefreshAsync(req.RefreshToken!, http, ct);

    // A6
    public async Task<LogoutResponse> LogoutAsync(long userId, CancellationToken ct)
    {
        await tokens.RevokeAllAsync(userId, ct);
        return new LogoutResponse(true);
    }

    private static AuthUser ToAuthUser(User u, bool isNewUser) => new(
        u.MemberId, u.FullName, u.Mobile, u.Email, u.Gender, u.ProfileFor,
        u.MobileVerified, u.EmailVerified, isNewUser, u.OnboardingStep);

    private async Task<string> NewMemberIdAsync(CancellationToken ct)
    {
        for (var i = 0; i < 20; i++)
        {
            var id = "DM-" + RandomNumberGenerator.GetInt32(100_000, 1_000_000);
            if (!await db.Users.AnyAsync(u => u.MemberId == id, ct)) return id;
        }
        throw new InvalidOperationException("Could not allocate a member id.");
    }
}
