using KalyanaVaran.Api.Common;
using KalyanaVaran.Api.Data;
using KalyanaVaran.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.JsonWebTokens;

namespace KalyanaVaran.Api.Middleware;

/// <summary>
/// Runs after UseAuthentication. A JWT stays valid for up to an hour, so this checks that the
/// user behind it still exists and isn't blocked. The status is cached for 60 seconds so we
/// don't hit the database on every call. Call <see cref="Invalidate"/> after blocking a user so
/// it takes effect immediately.
/// </summary>
public sealed class ActiveUserMiddleware(RequestDelegate next)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

    public async Task InvokeAsync(HttpContext ctx, AppDbContext db, IMemoryCache cache)
    {
        if (ctx.User.Identity?.IsAuthenticated == true &&
            long.TryParse(ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId))
        {
            var status = await cache.GetOrCreateAsync(Key(userId), async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheFor;
                return await db.Users.AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => u.Status)
                    .FirstOrDefaultAsync(ctx.RequestAborted);
            });

            if (status is null)
            {
                await ApiEnvelopeWriter.WriteErrorAsync(ctx, 401, ErrorCodes.Unauthorized,
                    "This account no longer exists. Please register again.");
                return;
            }
            if (status == UserStatus.Blocked)
            {
                await ApiEnvelopeWriter.WriteErrorAsync(ctx, 403, ErrorCodes.Forbidden,
                    "This account has been blocked. Please contact support.");
                return;
            }
        }

        await next(ctx);
    }

    public static void Invalidate(IMemoryCache cache, long userId) => cache.Remove(Key(userId));

    private static string Key(long userId) => $"user-status:{userId}";
}
