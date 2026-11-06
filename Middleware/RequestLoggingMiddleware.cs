using System.Diagnostics;
using Microsoft.IdentityModel.JsonWebTokens;

namespace KalyanaVaran.Api.Middleware;

/// <summary>
/// One log line per request:
///   HTTP POST /v1/auth/otp/verify → 200 in 42 ms | user=12 platform=web app=0.1.0 reqId=...
/// 5xx = Error, 4xx = Warning, else Information. Query strings and bodies are NOT logged
/// (they can contain OTPs, tokens and personal data).
/// </summary>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            await next(ctx);
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var status = ctx.Response.StatusCode;
            var path = ctx.Request.Path.Value ?? "/";

            // Docs UI / static files are noisy; keep them at Debug
            var quiet = path.StartsWith("/scalar") || path.StartsWith("/openapi") || path.StartsWith("/uploads");
            var level = status >= 500 ? LogLevel.Error
                : status >= 400 ? LogLevel.Warning
                : quiet ? LogLevel.Debug
                : LogLevel.Information;

            logger.Log(level,
                "HTTP {Method} {Path} → {StatusCode} in {ElapsedMs:0} ms | user={UserId} platform={Platform} app={AppVersion} reqId={RequestId}",
                ctx.Request.Method,
                path,
                status,
                elapsedMs,
                ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? "-",
                ctx.Request.Headers["X-Platform"].FirstOrDefault() ?? "-",
                ctx.Request.Headers["X-App-Version"].FirstOrDefault() ?? "-",
                RequestIdMiddleware.Get(ctx));
        }
    }
}
