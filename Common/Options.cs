namespace KalyanaVaran.Api.Common;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 30;
}

public sealed class OtpOptions
{
    public int ExpirySeconds { get; set; } = 600;
    public int ResendAfterSeconds { get; set; } = 30;
    public int MaxSendsPerHour { get; set; } = 5;
    public int MaxVerifyAttempts { get; set; } = 5;
    public string HashSecret { get; set; } = "";

    /// <summary>Development only: every OTP is this code (e.g. 842910, same as the UI mock) so you can test in Scalar.</summary>
    public string? DevFixedCode { get; set; }
}

public sealed class StorageOptions
{
    /// <summary>Public photos, served as static files. Must live under wwwroot.</summary>
    public string PublicRoot { get; set; } = "wwwroot/uploads";

    /// <summary>KYC documents. NOT served publicly.</summary>
    public string PrivateRoot { get; set; } = "private-uploads";

    /// <summary>Optional, e.g. https://cdn.kalyanavaran.com. Defaults to the API host.</summary>
    public string? PublicBaseUrl { get; set; }

    public long MaxPhotoBytes { get; set; } = 5 * 1024 * 1024;
    public int MaxPhotos { get; set; } = 5;
    public long MaxKycFileBytes { get; set; } = 10 * 1024 * 1024;
    public long MaxVideoBytes { get; set; } = 50 * 1024 * 1024;
}
