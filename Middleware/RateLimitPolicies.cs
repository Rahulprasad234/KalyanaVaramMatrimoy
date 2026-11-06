using System.Threading.RateLimiting;
using KalyanaVaran.Api.Common;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;

namespace KalyanaVaran.Api.Middleware;

public sealed class ApiRateLimitOptions
{
    /// <summary>Every request, per client IP.</summary>
    public int GlobalPerMinute { get; set; } = 300;

    /// <summary>/v1/auth/* per IP. Stops bots spamming register/OTP and burning SMS credits.</summary>
    public int AuthPerMinute { get; set; } = 10;

    /// <summary>Photo + KYC uploads, per logged-in user.</summary>
    public int UploadsPerMinute { get; set; } = 20;
}

/// <summary>
/// Uses ASP.NET Core's built-in rate limiter (no extra package).
/// Policies: global (all requests), "auth" and "uploads" (applied with [EnableRateLimiting]).
/// This works alongside the OTP limits in OtpService (30 s cooldown, 5 per hour per mobile).
/// </summary>
public static class RateLimitPolicies
{
    public const string AuthPolicy = "auth";
    public const string UploadsPolicy = "uploads";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration config)
    {
        var o = config.GetSection("RateLimits").Get<ApiRateLimitOptions>() ?? new ApiRateLimitOptions();

        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                RateLimitPartition.GetFixedWindowLimiter(ClientIp(ctx), _ => PerMinute(o.GlobalPerMinute)));

            options.AddPolicy(AuthPolicy, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(ClientIp(ctx), _ => PerMinute(o.AuthPerMinute)));

            options.AddPolicy(UploadsPolicy, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? ClientIp(ctx),
                    _ => PerMinute(o.UploadsPerMinute)));

            // Rejected → 429 in the standard envelope + Retry-After header
            options.OnRejected = async (context, cancellationToken) =>
            {
                var http = context.HttpContext;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();

                var isAuth = http.Request.Path.StartsWithSegments("/v1/auth");
                await ApiEnvelopeWriter.WriteErrorAsync(http, StatusCodes.Status429TooManyRequests,
                    isAuth ? ErrorCodes.OtpRateLimited : ErrorCodes.RateLimited,
                    "Too many requests. Please wait a minute and try again.");
            };
        });

        return services;
    }

    private static FixedWindowRateLimiterOptions PerMinute(int permits) => new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0
    };

    /// <summary>
    /// Behind a load balancer / reverse proxy, enable UseForwardedHeaders so this is the real
    /// client IP rather than the proxy's.
    /// </summary>
    private static string ClientIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
