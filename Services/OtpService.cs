using System.Security.Cryptography;
using System.Text;
using KalyanaVaran.Api.Common;
using KalyanaVaran.Api.Contracts;
using KalyanaVaran.Api.Data;
using KalyanaVaran.Api.Entities;
using KalyanaVaran.Api.Services.Delivery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KalyanaVaran.Api.Services;

public sealed record OtpVerifyResult(long UserId, bool EmailVerified);

/// <summary>
/// Generates, delivers (real SMS / email via Services/Delivery) and verifies OTPs.
/// - 6-digit code from a CSPRNG, stored only as HMAC-SHA256(secret, id:code)
/// - expiry, resend cooldown, max sends per hour, max wrong attempts, single use
/// - phone (sms / whatsapp) and email codes are separate rows with separate limits;
///   only the newest unused code of each kind is valid
/// - the code is never logged or returned
/// </summary>
public sealed class OtpService(
    AppDbContext db,
    ISmsSender sms,
    IEmailSender email,
    IOptions<OtpOptions> options,
    IOptions<SmsOptions> smsOptions,
    IOptions<EmailOptions> emailOptions,
    IHostEnvironment env,
    ILogger<OtpService> logger)
{
    public const string Email = "email";
    private readonly OtpOptions _o = options.Value;

    private int ValidMinutes => (int)Math.Ceiling(_o.ExpirySeconds / 60.0);

    public async Task<OtpSentResponse> SendAsync(User user, string channel, string purpose, HttpContext http, CancellationToken ct)
    {
        var mobile = user.Mobile ?? throw ApiException.Validation("This account has no mobile number.", "mobile");
        var isEmail = channel == Email;
        var destination = isEmail
            ? user.Email ?? throw ApiException.Validation("This account has no email address.", "email")
            : mobile;

        if (channel == "whatsapp" && !sms.SupportsWhatsApp)
            throw ApiException.Validation("WhatsApp OTP is not available yet. Please use SMS.", "channel");

        var now = DateTime.UtcNow;
        var hourAgo = now.AddHours(-1);

        // ---- 1. Cooldown + hourly limit (checked BEFORE anything is sent) ----
        var recent = await SameKind(db.OtpRequests.Where(o => o.Mobile == mobile && o.CreatedAt > hourAgo), isEmail)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);

        var latest = recent.FirstOrDefault();
        if (latest is not null && latest.ResendAfter > now)
        {
            var wait = (int)Math.Ceiling((latest.ResendAfter - now).TotalSeconds);
            throw ApiException.OtpRateLimited($"Please wait {wait} seconds before requesting a new OTP.");
        }
        if (recent.Count >= _o.MaxSendsPerHour)
            throw ApiException.OtpRateLimited("Too many OTP requests. Please try again after some time.");

        // ---- 2. Generate ----
        var id = "otp_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        var code = UseDevFixedCode(isEmail)
            ? _o.DevFixedCode!
            : RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        // ---- 3. Deliver first: if the provider fails, nothing is stored and the old code stays valid ----
        string? providerMessageId;
        try
        {
            providerMessageId = isEmail
                ? await email.SendOtpAsync(destination, user.FullName, code, ValidMinutes, ct)
                : await sms.SendOtpAsync(mobile, channel, code, ValidMinutes, ct);
        }
        catch (OtpDeliveryException ex)
        {
            logger.LogWarning(ex, "OTP delivery failed: channel={Channel} to={Destination} providerDown={Down}",
                channel, OtpText.Mask(destination), ex.ProviderUnavailable);
            throw DeliveryError(isEmail, ex.ProviderUnavailable);
        }

        // ---- 4. Store hash; only the newest code of this kind stays valid ----
        foreach (var old in recent.Where(o => o.ConsumedAt is null)) old.ConsumedAt = now;

        db.OtpRequests.Add(new OtpRequest
        {
            Id = id,
            UserId = user.Id,
            Mobile = mobile,
            Channel = channel,
            Destination = destination,
            Purpose = purpose,
            OtpHash = Hash(id, code),
            ExpiresAt = now.AddSeconds(_o.ExpirySeconds),
            ResendAfter = now.AddSeconds(_o.ResendAfterSeconds),
            ProviderMessageId = providerMessageId is null ? null : OtpText.Truncate(providerMessageId, 100),
            Ip = http.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = now
        });
        await db.SaveChangesAsync(ct);

        logger.LogInformation("OTP sent: id={OtpId} channel={Channel} to={Destination} purpose={Purpose}",
            id, channel, OtpText.Mask(destination), purpose);

        return new OtpSentResponse(id, mobile, channel, _o.ExpirySeconds, _o.ResendAfterSeconds);
    }

    /// <summary>
    /// Checks the mobile code (required) and the email code (optional). Both are checked before
    /// either is consumed. The newest active code for the mobile is used, so an old otpRequestId
    /// after "Resend OTP" still works with the newest code.
    /// </summary>
    public async Task<OtpVerifyResult> VerifyAsync(string otpRequestId, string mobile, string code, string? emailCode,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Same request verified before → explicit "already used"
        var requested = await db.OtpRequests.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == otpRequestId && o.Mobile == mobile, ct);
        if (requested?.VerifiedAt is not null)
            throw FieldError(400, ErrorCodes.OtpAlreadyUsed, "This OTP has already been used. Please request a new one.", "otp");

        var phone = await ActiveAsync(mobile, isEmail: false, ct);
        if (phone is null)
            throw requested is not null
                ? ApiException.OtpExpired("This OTP is no longer valid. Please request a new one.")
                : ApiException.OtpInvalid("This OTP request is not valid. Please request a new OTP.");
        EnsureUsable(phone, now, "otp", "OTP");

        OtpRequest? mail = null;
        if (!string.IsNullOrWhiteSpace(emailCode))
        {
            mail = await ActiveAsync(mobile, isEmail: true, ct)
                   ?? throw FieldError(400, ErrorCodes.OtpExpired, "The email OTP has expired. Please request a new one.", "emailOtp");
            EnsureUsable(mail, now, "emailOtp", "email OTP");
        }

        if (!Matches(phone, code)) await FailAsync(phone, "otp", "mobile OTP", ct);
        if (mail is not null && !Matches(mail, emailCode!)) await FailAsync(mail, "emailOtp", "email OTP", ct);

        // Single use: consumed + verified
        phone.ConsumedAt = now;
        phone.VerifiedAt = now;
        if (mail is not null)
        {
            mail.ConsumedAt = now;
            mail.VerifiedAt = now;
        }
        await db.SaveChangesAsync(ct);

        return new OtpVerifyResult(phone.UserId, mail is not null);
    }

    /// <summary>The fixed code is used ONLY in Development AND only for a channel whose provider is Console.</summary>
    private bool UseDevFixedCode(bool isEmail)
    {
        if (!env.IsDevelopment() || string.IsNullOrWhiteSpace(_o.DevFixedCode)) return false;
        var provider = isEmail ? emailOptions.Value.Provider : smsOptions.Value.Provider;
        return provider.Equals("Console", StringComparison.OrdinalIgnoreCase);
    }

    private static ApiException DeliveryError(bool isEmail, bool providerDown) => isEmail
        ? new ApiException(providerDown ? 503 : 502, ErrorCodes.OtpDeliveryFailed,
            providerDown
                ? "Our email service is temporarily unavailable. Please try again in a few minutes or use the mobile OTP."
                : "We couldn't send the OTP to your email address. Please check the email and try again.",
            new Dictionary<string, string> { ["email"] = "OTP email could not be delivered." })
        : new ApiException(providerDown ? 503 : 502, ErrorCodes.OtpDeliveryFailed,
            providerDown
                ? "Our SMS service is temporarily unavailable. Please try again in a few minutes."
                : "We couldn't send the OTP to this mobile number. Please check the number and try again.",
            new Dictionary<string, string> { ["mobile"] = "OTP SMS could not be delivered." });

    private Task<OtpRequest?> ActiveAsync(string mobile, bool isEmail, CancellationToken ct) =>
        SameKind(db.OtpRequests.Where(o => o.Mobile == mobile && o.ConsumedAt == null), isEmail)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(ct);

    private static IQueryable<OtpRequest> SameKind(IQueryable<OtpRequest> q, bool isEmail) =>
        isEmail ? q.Where(o => o.Channel == Email) : q.Where(o => o.Channel != Email);

    private void EnsureUsable(OtpRequest otp, DateTime now, string field, string label)
    {
        if (otp.Attempts >= _o.MaxVerifyAttempts)
            throw FieldError(429, ErrorCodes.OtpRateLimited, $"Too many wrong attempts. Please request a new {label}.", field);
        if (otp.ExpiresAt <= now)
            throw FieldError(400, ErrorCodes.OtpExpired, $"This {label} has expired. Please request a new one.", field);
    }

    private async Task FailAsync(OtpRequest otp, string field, string label, CancellationToken ct)
    {
        otp.Attempts++;
        await db.SaveChangesAsync(ct);
        var left = _o.MaxVerifyAttempts - otp.Attempts;
        throw left > 0
            ? FieldError(400, ErrorCodes.OtpInvalid,
                $"The {label} you entered is incorrect. {left} attempt{(left == 1 ? "" : "s")} left.", field)
            : FieldError(429, ErrorCodes.OtpRateLimited, $"Too many wrong attempts. Please request a new {label}.", field);
    }

    private bool Matches(OtpRequest otp, string code) =>
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(otp.OtpHash), Convert.FromHexString(Hash(otp.Id, code)));

    private static ApiException FieldError(int status, string code, string message, string field) =>
        new(status, code, message, new Dictionary<string, string> { [field] = message });

    private string Hash(string otpId, string code) =>
        Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(_o.HashSecret), Encoding.UTF8.GetBytes($"{otpId}:{code}")));
}
