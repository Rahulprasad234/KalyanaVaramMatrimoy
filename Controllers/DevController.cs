using KalyanaVaran.Api.Common;
using KalyanaVaran.Api.Contracts;
using KalyanaVaran.Api.Data;
using KalyanaVaran.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace KalyanaVaran.Api.Controllers;

/// <summary>Testing helpers. Return 404 outside the Development environment.</summary>
[Route("v1/dev")]
[AllowAnonymous]
[Tags("Dev tools")]
public sealed class DevController(MediaService media, AppDbContext db, IWebHostEnvironment env) : ApiControllerBase
{
    /// <summary>Checks the Entity Framework connection to the database and counts rows per table.</summary>
    [HttpGet("db-check")]
    [EndpointSummary("DEV · Check database connection (EF Core)")]
    public async Task<ActionResult<ApiResponse<DbCheckResponse>>> DbCheck(CancellationToken ct)
    {
        if (!env.IsDevelopment()) throw ApiException.NotFound();

        var conn = db.Database.GetDbConnection();
        try
        {
            var counts = new Dictionary<string, int>
            {
                ["Users"] = await db.Users.CountAsync(ct),
                ["OtpRequests"] = await db.OtpRequests.CountAsync(ct),
                ["RefreshTokens"] = await db.RefreshTokens.CountAsync(ct),
                ["OnboardingSteps"] = await db.OnboardingSteps.CountAsync(ct),
                ["Profiles"] = await db.Profiles.CountAsync(ct),
                ["Siblings"] = await db.Siblings.CountAsync(ct),
                ["PartnerPreferences"] = await db.PartnerPreferences.CountAsync(ct),
                ["Photos"] = await db.Photos.CountAsync(ct),
                ["KycVerifications"] = await db.KycVerifications.CountAsync(ct)
            };
            var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();

            return Envelope(new DbCheckResponse(true, conn.DataSource, conn.Database, counts, applied, pending));
        }
        catch (Exception ex) when (ex is SqlException || ex.InnerException is SqlException)
        {
            var sql = ex as SqlException ?? (SqlException)ex.InnerException!;
            var hint = sql.Number switch
            {
                40615 => "Your IP is blocked by the Azure SQL firewall. Add it in Azure portal → SQL server → Networking.",
                18456 => "Login failed: check User ID / Password in the connection string (dotnet user-secrets).",
                4060 => "Database name not found: check Initial Catalog=KalyanaVaram.",
                208 => "A table is missing: run Database/KalyanaVaram_AzureSQL.sql.",
                207 => "A column is missing: run Database/KalyanaVaram_AzureSQL.sql (UPGRADE section).",
                _ => "See the SQL error message."
            };
            throw new ApiException(503, ErrorCodes.InternalError, $"Database error {sql.Number}: {hint} ({sql.Message})");
        }
    }

    /// <summary>
    /// Sends ONE test OTP (random code, not stored, not valid for login) through the configured
    /// SMS or email provider, to check your keys / DLT template / SMTP login.
    /// </summary>
    [HttpPost("test-delivery")]
    [EndpointSummary("DEV · Test SMS / email provider")]
    public async Task<ActionResult<ApiResponse<DevTestDeliveryResponse>>> TestDelivery(
        DevTestDeliveryRequest body,
        [FromServices] Services.Delivery.ISmsSender sms,
        [FromServices] Services.Delivery.IEmailSender email,
        [FromServices] Microsoft.Extensions.Options.IOptions<Services.Delivery.SmsOptions> smsOptions,
        [FromServices] Microsoft.Extensions.Options.IOptions<Services.Delivery.EmailOptions> emailOptions,
        CancellationToken ct)
    {
        if (!env.IsDevelopment()) throw ApiException.NotFound();

        var code = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var isEmail = body.Channel == "email";
        try
        {
            var id = isEmail
                ? await email.SendOtpAsync(body.To!, "Test User", code, 10, ct)
                : await sms.SendOtpAsync(body.To!, body.Channel, code, 10, ct);
            var provider = isEmail ? emailOptions.Value.Provider : smsOptions.Value.Provider;
            return Envelope(new DevTestDeliveryResponse(true, provider, id,
                provider.Equals("Console", StringComparison.OrdinalIgnoreCase)
                    ? "Console provider: nothing was really sent. Set a real provider to test delivery."
                    : $"Sent. Check the {(isEmail ? "inbox (and spam folder)" : "phone")}."));
        }
        catch (Services.Delivery.OtpDeliveryException ex)
        {
            throw new ApiException(ex.ProviderUnavailable ? 503 : 502, ErrorCodes.OtpDeliveryFailed,
                $"Provider error: {ex.Message} {ex.InnerException?.Message}".Trim());
        }
    }

    [HttpPost("kyc/{memberId}/review")]
    [EndpointSummary("DEV · Approve / reject a member's KYC")]
    public async Task<ActionResult<ApiResponse<VerificationResponse>>> ReviewKyc(
        string memberId, DevKycReviewRequest body, CancellationToken ct)
    {
        if (!env.IsDevelopment()) throw ApiException.NotFound();
        return Envelope(await media.ReviewKycAsync(memberId, body.Status!, body.Reason, ct));
    }
}
