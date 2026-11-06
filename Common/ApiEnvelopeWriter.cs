using Microsoft.Extensions.Options;

namespace KalyanaVaran.Api.Common;

/// <summary>Writes the standard error envelope from middleware (outside MVC).</summary>
public static class ApiEnvelopeWriter
{
    public static Task WriteErrorAsync(HttpContext ctx, int status, string code, string message,
        IDictionary<string, string>? details = null)
    {
        ctx.Response.StatusCode = status;
        var json = ctx.RequestServices.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>()
            .Value.JsonSerializerOptions;
        var body = new ApiResponse<object>(false, null, new ApiError(code, message, details), ApiMeta.For(ctx));
        return ctx.Response.WriteAsJsonAsync(body, json);
    }

    /// <summary>Used for empty-body status codes (401 from JWT, 404 unknown route, 405, ...).</summary>
    public static (string Code, string Message) ForStatus(int status) => status switch
    {
        400 => (ErrorCodes.ValidationError, "The request is invalid."),
        401 => (ErrorCodes.Unauthorized, "Your session has expired. Please log in again."),
        403 => (ErrorCodes.Forbidden, "You are not allowed to do this."),
        404 => (ErrorCodes.NotFound, "Not found."),
        405 => (ErrorCodes.NotFound, "This method is not supported for this URL."),
        413 => (ErrorCodes.PayloadTooLarge, "The request is too large."),
        415 => (ErrorCodes.UnsupportedMediaType, "Unsupported content type."),
        429 => (ErrorCodes.RateLimited, "Too many requests. Please slow down."),
        _ => (ErrorCodes.InternalError, "Something went wrong on our side. Please try again.")
    };
}
