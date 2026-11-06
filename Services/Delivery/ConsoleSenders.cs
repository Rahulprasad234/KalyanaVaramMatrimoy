namespace KalyanaVaran.Api.Services.Delivery;

/// <summary>
/// DEVELOPMENT ONLY. Sends nothing and never logs the code; use Otp:DevFixedCode (e.g. 842910) to test.
/// Startup refuses to run with this provider outside Development.
/// </summary>
public sealed class ConsoleSmsSender(ILogger<ConsoleSmsSender> logger) : ISmsSender
{
    public bool SupportsWhatsApp => true;

    public Task<string?> SendOtpAsync(string mobile, string channel, string code, int validMinutes, CancellationToken ct)
    {
        logger.LogInformation("[DEV console provider] {Channel} OTP to {Mobile} not sent for real; use Otp:DevFixedCode.",
            channel, OtpText.Mask(mobile));
        return Task.FromResult<string?>("console-" + Guid.NewGuid().ToString("N")[..12]);
    }
}

/// <summary>DEVELOPMENT ONLY. See <see cref="ConsoleSmsSender"/>.</summary>
public sealed class ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) : IEmailSender
{
    public Task<string?> SendOtpAsync(string email, string? name, string code, int validMinutes, CancellationToken ct)
    {
        logger.LogInformation("[DEV console provider] Email OTP to {Email} not sent for real; use Otp:DevFixedCode.",
            OtpText.Mask(email));
        return Task.FromResult<string?>("console-" + Guid.NewGuid().ToString("N")[..12]);
    }
}
