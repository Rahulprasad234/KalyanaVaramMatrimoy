namespace KalyanaVaran.Api.Middleware;

/// <summary>
/// Adds standard security headers to every response.
/// API responses (/v1/...) also get Cache-Control: no-store because they contain personal data
/// that must not be cached by browsers or proxies.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext ctx)
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";                 // no MIME sniffing of uploads
        h["X-Frame-Options"] = "DENY";                           // no click-jacking via iframes
        h["Referrer-Policy"] = "no-referrer";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        h["Cross-Origin-Opener-Policy"] = "same-origin";

        if (ctx.Request.Path.StartsWithSegments("/v1"))
        {
            h.CacheControl = "no-store";
            h.Pragma = "no-cache";
        }

        return next(ctx);
    }
}
