namespace KalyanaVaran.Api.Common;

/// <summary>Throw from anywhere; ExceptionMiddleware turns it into the error envelope.</summary>
public sealed class ApiException(int status, string code, string message, IDictionary<string, string>? details = null)
    : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public IDictionary<string, string>? Details { get; } = details;

    public static ApiException Validation(string message, string? field = null) =>
        new(400, ErrorCodes.ValidationError, message,
            field is null ? null : new Dictionary<string, string> { [field] = message });

    public static ApiException Unauthorized(string message = "Your session has expired. Please log in again.") =>
        new(401, ErrorCodes.Unauthorized, message);

    public static ApiException Forbidden(string message = "You are not allowed to do this.") =>
        new(403, ErrorCodes.Forbidden, message);

    public static ApiException NotFound(string message = "Not found.") =>
        new(404, ErrorCodes.NotFound, message);

    public static ApiException Conflict(string message, string? field = null) =>
        new(409, ErrorCodes.Conflict, message,
            field is null ? null : new Dictionary<string, string> { [field] = message });

    public static ApiException OtpInvalid(string message = "Incorrect OTP. Please try again.") =>
        new(400, ErrorCodes.OtpInvalid, message);

    public static ApiException OtpExpired(string message = "This OTP has expired. Please request a new one.") =>
        new(400, ErrorCodes.OtpExpired, message);

    public static ApiException OtpRateLimited(string message) =>
        new(429, ErrorCodes.OtpRateLimited, message);

    public static ApiException PayloadTooLarge(string message) =>
        new(413, ErrorCodes.PayloadTooLarge, message);

    public static ApiException UnsupportedMediaType(string message) =>
        new(415, ErrorCodes.UnsupportedMediaType, message);
}
