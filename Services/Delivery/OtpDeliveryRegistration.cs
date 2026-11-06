using KalyanaVaran.Api.Common;

namespace KalyanaVaran.Api.Services.Delivery;

public static class OtpDeliveryRegistration
{
    /// <summary>Flat env var names from the requirement → .NET config keys.</summary>
    private static readonly Dictionary<string, string> EnvAliases = new()
    {
        ["SMS_PROVIDER"] = "Sms:Provider",
        ["SMS_API_KEY"] = "Sms:ApiKey",
        ["SMS_API_SECRET"] = "Sms:ApiSecret",
        ["SMS_SENDER_ID"] = "Sms:SenderId",
        ["SMS_TEMPLATE_ID"] = "Sms:TemplateId",
        ["SMS_OTP_VARIABLE"] = "Sms:OtpVariable",
        ["SMS_EXPIRY_VARIABLE"] = "Sms:ExpiryVariable",
        ["SMS_WHATSAPP_FROM"] = "Sms:WhatsAppFrom",
        ["EMAIL_PROVIDER"] = "Email:Provider",
        ["EMAIL_HOST"] = "Email:Host",
        ["EMAIL_PORT"] = "Email:Port",
        ["EMAIL_USERNAME"] = "Email:Username",
        ["EMAIL_PASSWORD"] = "Email:Password",
        ["EMAIL_FROM"] = "Email:From",
        ["EMAIL_FROM_NAME"] = "Email:FromName",
        ["EMAIL_API_KEY"] = "Email:ApiKey",
        ["OTP_EXPIRY_SECONDS"] = "Otp:ExpirySeconds",
        ["OTP_HASH_SECRET"] = "Otp:HashSecret"
    };

    /// <summary>Lets SMS_PROVIDER, EMAIL_HOST … (from the requirement) override appsettings / user-secrets.</summary>
    public static void AddOtpDeliveryEnvironmentAliases(this IConfigurationBuilder config)
    {
        var values = EnvAliases
            .Select(a => (Key: a.Value, Value: Environment.GetEnvironmentVariable(a.Key)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => new KeyValuePair<string, string?>(x.Key, x.Value));
        config.AddInMemoryCollection(values);
    }

    /// <summary>Registers the configured SMS + email senders. Falls back to the Console (mock) sender,
    /// with a logged warning, when the chosen provider is missing required settings — it never blocks startup.</summary>
    public static IServiceCollection AddOtpDelivery(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        services.Configure<SmsOptions>(config.GetSection("Sms"));
        services.Configure<EmailOptions>(config.GetSection("Email"));

        var sms = config.GetSection("Sms").Get<SmsOptions>() ?? new SmsOptions();
        var email = config.GetSection("Email").Get<EmailOptions>() ?? new EmailOptions();

        using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var logger = loggerFactory.CreateLogger("OtpDelivery");

        var smsProvider = ResolveSmsProvider(sms, logger);
        switch (smsProvider)
        {
            case "msg91":
                services.AddHttpClient<ISmsSender, Msg91SmsSender>(c =>
                {
                    c.BaseAddress = new Uri("https://control.msg91.com/");
                    c.Timeout = TimeSpan.FromSeconds(15);
                });
                break;
            case "fast2sms":
                services.AddHttpClient<ISmsSender, Fast2SmsSender>(c =>
                {
                    c.BaseAddress = new Uri("https://www.fast2sms.com/");
                    c.Timeout = TimeSpan.FromSeconds(15);
                });
                break;
            case "twilio":
                services.AddHttpClient<ISmsSender, TwilioSmsSender>(c =>
                {
                    c.BaseAddress = new Uri("https://api.twilio.com/");
                    c.Timeout = TimeSpan.FromSeconds(15);
                });
                break;
            default:
                services.AddSingleton<ISmsSender, ConsoleSmsSender>();
                break;
        }

        var emailProvider = ResolveEmailProvider(email, logger);
        switch (emailProvider)
        {
            case "smtp":
                services.AddSingleton<IEmailSender, SmtpEmailSender>();
                break;
            case "sendgrid":
                services.AddHttpClient<IEmailSender, SendGridEmailSender>(c =>
                {
                    c.BaseAddress = new Uri("https://api.sendgrid.com/");
                    c.Timeout = TimeSpan.FromSeconds(15);
                });
                break;
            default:
                services.AddSingleton<IEmailSender, ConsoleEmailSender>();
                break;
        }

        return services;
    }

    /// <summary>Returns the SMS provider to actually use: the configured one if fully set up, otherwise "console".</summary>
    private static string ResolveSmsProvider(SmsOptions sms, ILogger logger)
    {
        var provider = sms.Provider.Trim().ToLowerInvariant();
        var missing = new List<string>();

        void Need(string? value, string configKey) { if (string.IsNullOrWhiteSpace(value)) missing.Add(configKey); }

        switch (provider)
        {
            case "msg91":
                Need(sms.ApiKey, "Sms:ApiKey = MSG91 AuthKey");
                Need(sms.TemplateId, "Sms:TemplateId = MSG91 Flow/DLT template id");
                break;
            case "fast2sms":
                Need(sms.ApiKey, "Sms:ApiKey = Fast2SMS API key");
                if (!string.IsNullOrWhiteSpace(sms.TemplateId))
                    Need(sms.SenderId, "Sms:SenderId = approved DLT sender id (header)");
                break;
            case "twilio":
                Need(sms.ApiKey, "Sms:ApiKey = Twilio Account SID");
                Need(sms.ApiSecret, "Sms:ApiSecret = Twilio Auth Token");
                Need(sms.SenderId, "Sms:SenderId = Twilio number or Messaging Service SID");
                break;
            case "console":
                return "console";
            default:
                logger.LogWarning("Sms:Provider '{Provider}' is unknown; falling back to Console (mock) SMS.", sms.Provider);
                return "console";
        }

        if (missing.Count == 0) return provider;

        logger.LogWarning(
            "Sms:Provider '{Provider}' is missing settings ({Missing}); falling back to Console (mock) SMS until they're set. See docs/OTP_DELIVERY.md.",
            sms.Provider, string.Join(", ", missing));
        return "console";
    }

    /// <summary>Returns the email provider to actually use: the configured one if fully set up, otherwise "console".</summary>
    private static string ResolveEmailProvider(EmailOptions email, ILogger logger)
    {
        var provider = email.Provider.Trim().ToLowerInvariant();
        var missing = new List<string>();

        void Need(string? value, string configKey) { if (string.IsNullOrWhiteSpace(value)) missing.Add(configKey); }

        switch (provider)
        {
            case "smtp":
                Need(email.Host, "Email:Host");
                Need(email.From, "Email:From");
                if (!string.IsNullOrWhiteSpace(email.Username)) Need(email.Password, "Email:Password");
                break;
            case "sendgrid":
                Need(email.ApiKey, "Email:ApiKey = SendGrid API key");
                Need(email.From, "Email:From (verified sender)");
                break;
            case "console":
                return "console";
            default:
                logger.LogWarning("Email:Provider '{Provider}' is unknown; falling back to Console (mock) email.", email.Provider);
                return "console";
        }

        if (missing.Count == 0) return provider;

        logger.LogWarning(
            "Email:Provider '{Provider}' is missing settings ({Missing}); falling back to Console (mock) email until they're set. See docs/OTP_DELIVERY.md.",
            email.Provider, string.Join(", ", missing));
        return "console";
    }
}
