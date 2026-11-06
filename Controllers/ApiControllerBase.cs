using KalyanaVaran.Api.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace KalyanaVaran.Api.Controllers;

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>Wraps data in the standard { success, data, error, meta } envelope.</summary>
    protected ApiResponse<T> Envelope<T>(T data) => ApiResponse<T>.Ok(data, HttpContext);

    /// <summary>users.id from the JWT "sub" claim.</summary>
    protected long CurrentUserId =>
        long.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
            ? id
            : throw ApiException.Unauthorized();

    protected string BaseUrl => $"{Request.Scheme}://{Request.Host}";
}
