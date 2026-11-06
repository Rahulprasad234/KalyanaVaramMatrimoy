using KalyanaVaran.Api.Middleware;

namespace KalyanaVaran.Api.Common;

/// <summary>meta block present on every response.</summary>
public sealed record ApiMeta(string RequestId, DateTime Timestamp)
{
    public static ApiMeta For(HttpContext ctx) => new(RequestIdMiddleware.Get(ctx), DateTime.UtcNow);
}

/// <summary>error block on failures.</summary>
public sealed record ApiError(string Code, string Message, IDictionary<string, string>? Details = null);

/// <summary>Standard envelope: { success, data, error, meta }.</summary>
public sealed record ApiResponse<T>(bool Success, T? Data, ApiError? Error, ApiMeta Meta)
{
    public static ApiResponse<T> Ok(T data, HttpContext ctx) => new(true, data, null, ApiMeta.For(ctx));
}

public static class ErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string Conflict = "CONFLICT";
    public const string OtpInvalid = "OTP_INVALID";
    public const string OtpExpired = "OTP_EXPIRED";
    public const string OtpRateLimited = "OTP_RATE_LIMITED";
    public const string OtpAlreadyUsed = "OTP_ALREADY_USED";
    public const string OtpDeliveryFailed = "OTP_DELIVERY_FAILED";
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";
    public const string UnsupportedMediaType = "UNSUPPORTED_MEDIA_TYPE";
    public const string RateLimited = "RATE_LIMITED";
    public const string InternalError = "INTERNAL_ERROR";
}
