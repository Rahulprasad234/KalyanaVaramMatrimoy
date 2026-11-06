namespace KalyanaVaran.Api.Services.Delivery;

/// <summary>SMS settings. Section "Sms" or env vars SMS_PROVIDER, SMS_API_KEY, SMS_API_SECRET, SMS_SENDER_ID, SMS_TEMPLATE_ID.</summary>
public sealed class SmsOptions
{
    /// <summary>Fast2Sms | Msg91 | Twilio | Console (Console = Development only, sends nothing).</summary>
    public string Provider { get; set; } = "Console";

    /// <summary>Fast2SMS: API key. MSG91: AuthKey. Twilio: Account SID.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Twilio: Auth Token. (Not used by MSG91.)</summary>
    public string? ApiSecret { get; set; }

    /// <summary>Twilio: sending number (+1…) or Messaging Service SID (MG…). MSG91: DLT sender id (optional, set on the template).</summary>
    public string? SenderId { get; set; }

    /// <summary>MSG91: Flow / DLT template id. Fast2SMS: DLT message id (empty = "otp" route).</summary>
    public string? TemplateId { get; set; }

    /// <summary>MSG91: name of the OTP variable in the template, e.g. ##otp##.</summary>
    public string OtpVariable { get; set; } = "otp";

    /// <summary>MSG91: optional template variable for the validity in minutes, e.g. ##minutes##.</summary>
    public string? ExpiryVariable { get; set; }

    /// <summary>Twilio only: WhatsApp-enabled sender number (+1…). Empty = WhatsApp channel disabled.</summary>
    public string? WhatsAppFrom { get; set; }
}

/// <summary>Email settings. Section "Email" or env vars EMAIL_PROVIDER, EMAIL_HOST, EMAIL_PORT, EMAIL_USERNAME, EMAIL_PASSWORD, EMAIL_FROM, EMAIL_API_KEY.</summary>
public sealed class EmailOptions
{
    /// <summary>Smtp | SendGrid | Console (Console = Development only, sends nothing).</summary>
    public string Provider { get; set; } = "Console";

    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>Sender address, e.g. no-reply@kalyanavaran.com (must be verified with the provider).</summary>
    public string? From { get; set; }
    public string FromName { get; set; } = "KalyanaVaran Matrimony";

    /// <summary>SendGrid API key.</summary>
    public string? ApiKey { get; set; }
}

public interface ISmsSender
{
    /// <summary>True when the "whatsapp" channel can be used.</summary>
    bool SupportsWhatsApp { get; }

    /// <summary>Sends the code. Returns the provider's message id. Throws <see cref="OtpDeliveryException"/> on failure.</summary>
    Task<string?> SendOtpAsync(string mobile, string channel, string code, int validMinutes, CancellationToken ct);
}

public interface IEmailSender
{
    /// <summary>Sends the code. Returns the provider's message id. Throws <see cref="OtpDeliveryException"/> on failure.</summary>
    Task<string?> SendOtpAsync(string email, string? name, string code, int validMinutes, CancellationToken ct);
}

/// <summary>
/// Delivery failed. <see cref="ProviderUnavailable"/> = provider down / unreachable / misconfigured (HTTP 503),
/// otherwise the provider rejected this message (HTTP 502). The message must never contain the OTP.
/// </summary>
public sealed class OtpDeliveryException(string message, bool providerUnavailable, Exception? inner = null)
    : Exception(message, inner)
{
    public bool ProviderUnavailable { get; } = providerUnavailable;
}

public static class OtpText
{
    public static string Sms(string code, int minutes) =>
        $"{code} is your KalyanaVaran Matrimony verification code. Valid for {minutes} minutes. Do not share it with anyone.";

    /// <summary>For logs: +91******3210 / ra***@example.com. Never log full contact details or codes.</summary>
    public static string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "-";
        var at = value.IndexOf('@');
        if (at > 0) return value[..Math.Min(2, at)] + "***" + value[at..];
        return value.Length <= 7 ? "***" : value[..3] + new string('*', value.Length - 7) + value[^4..];
    }

    public static string Truncate(string? s, int max = 300) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";
}
