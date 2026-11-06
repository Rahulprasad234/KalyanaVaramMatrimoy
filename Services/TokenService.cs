using System.Security.Cryptography;
using System.Text;
using KalyanaVaran.Api.Common;
using KalyanaVaran.Api.Contracts;
using KalyanaVaran.Api.Data;
using KalyanaVaran.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace KalyanaVaran.Api.Services;

public sealed class TokenService(AppDbContext db, IOptions<JwtOptions> options)
{
    private readonly JwtOptions _o = options.Value;

    public async Task<AuthTokens> IssueAsync(User user, HttpContext http, CancellationToken ct)
    {
        var (raw, entity) = NewRefreshToken(user.Id, http);
        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync(ct);
        return new AuthTokens(CreateAccessToken(user), raw, "Bearer", _o.AccessTokenMinutes * 60);
    }

    /// <summary>Rotates the refresh token. Reusing a revoked token revokes the whole family.</summary>
    public async Task<AuthTokens> RefreshAsync(string rawToken, HttpContext http, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var hash = Hash(rawToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
                    ?? throw ApiException.Unauthorized();

        if (token.RevokedAt is not null)
        {
            // Possible theft: a rotated token was used again. Kill every session of this user.
            await RevokeAllAsync(token.UserId, ct);
            throw ApiException.Unauthorized();
        }
        if (token.ExpiresAt <= now) throw ApiException.Unauthorized();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == token.UserId, ct);
        if (user is null || user.Status != UserStatus.Active) throw ApiException.Unauthorized();

        var (raw, replacement) = NewRefreshToken(user.Id, http);
        db.RefreshTokens.Add(replacement);
        token.RevokedAt = now;
        await db.SaveChangesAsync(ct);

        token.ReplacedById = replacement.Id;
        await db.SaveChangesAsync(ct);

        return new AuthTokens(CreateAccessToken(user), raw, "Bearer", _o.AccessTokenMinutes * 60);
    }

    public async Task RevokeAllAsync(long userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var active = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in active) t.RevokedAt = now;
        await db.SaveChangesAsync(ct);
    }

    private string CreateAccessToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_o.SigningKey));
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _o.Issuer,
            Audience = _o.Audience,
            Expires = DateTime.UtcNow.AddMinutes(_o.AccessTokenMinutes),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                ["member_id"] = user.MemberId
            },
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private (string Raw, RefreshToken Entity) NewRefreshToken(long userId, HttpContext http)
    {
        var raw = "rt_" + Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var entity = new RefreshToken
        {
            UserId = userId,
            TokenHash = Hash(raw),
            ExpiresAt = DateTime.UtcNow.AddDays(_o.RefreshTokenDays),
            UserAgent = Truncate(http.Request.Headers.UserAgent.ToString(), 300),
            Ip = http.Connection.RemoteIpAddress?.ToString()
        };
        return (raw, entity);
    }

    private static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    private static string? Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? null : s.Length <= max ? s : s[..max];
}
