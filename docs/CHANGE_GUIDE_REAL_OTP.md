# Change guide: real SMS + email OTP

Apply these changes to the project you already have open in VS Code
(`D:\KVM-API-2\kalyanavaran-api`). Work through the steps in order and tick each box.

**How to use this guide**
- **NEW FILE**: right-click the folder in VS Code Explorer → *New File* → type the exact name → paste the whole block → **Ctrl+S**.
- **REPLACE FILE**: open the file → **Ctrl+A** → paste the whole block → **Ctrl+S**.
- **EDIT**: find the "Find" text with **Ctrl+F** and change only that part.

| Step | What | Type |
|---|---|---|
| 0 | Stop the API and make a backup | — |
| 1 | Database: add 2 columns | SQL |
| 2 | `KalyanaVaran.Api.csproj` | REPLACE |
| 3 | `appsettings.json`, `appsettings.Development.json` | REPLACE |
| 4 | `Services/Delivery/` (9 files) | NEW |
| 5 | `Services/OtpService.cs`, `Services/AuthService.cs` | REPLACE |
| 6 | `Controllers/AuthController.cs`, `Controllers/DevController.cs` | REPLACE |
| 7 | `Entities/Entities.cs`, `Data/AppDbContext.cs`, `Common/ApiResponse.cs`, `Contracts/ProfileContracts.cs`, `Program.cs` | EDIT |
| 8 | `.env.example` | NEW |
| 9 | Build, run and test | — |

---

## Step 0: Before you start
- [ ] Stop the API: **Shift+F5**, or **Ctrl+C** in the terminal.
- [ ] Make a backup: copy the folder to `kalyanavaran-api-backup`, or if you use Git, run `git add -A && git commit -m "before real OTP"`.

---

## Step 1: Database (SSMS or the VS Code SQL panel)
- [ ] Select the **KalyanaVaram** database, paste this script and run it (**F5**). It is safe to run more than once.

```sql
/* =====================================================================
   Upgrade: real SMS / email OTP delivery (Nov 2026)
   Adds 2 columns to dbo.OtpRequests. Safe to run more than once.
   SSMS: select the KalyanaVaram database in the toolbar, then F5.
   ===================================================================== */

IF COL_LENGTH(N'dbo.OtpRequests', N'VerifiedAt') IS NULL
    ALTER TABLE dbo.OtpRequests ADD VerifiedAt DATETIME2 NULL;          -- set only when the code was entered correctly
GO

IF COL_LENGTH(N'dbo.OtpRequests', N'ProviderMessageId') IS NULL
    ALTER TABLE dbo.OtpRequests ADD ProviderMessageId NVARCHAR(100) NULL; -- MSG91 / Twilio / SendGrid / SMTP message id
GO

-- Check: both rows should show a length (not NULL)
SELECT COL_LENGTH(N'dbo.OtpRequests', N'VerifiedAt')        AS VerifiedAt,
       COL_LENGTH(N'dbo.OtpRequests', N'ProviderMessageId') AS ProviderMessageId;
GO
```
✅ The last query shows two numbers, not NULL.

---

## Step 2: REPLACE `KalyanaVaran.Api.csproj`
This adds **MailKit** for sending SMTP email.

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>KalyanaVaran.Api</RootNamespace>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);1591</NoWarn>
    <UserSecretsId>kalyanavaran-api-dev</UserSecretsId>
    <!-- Don't create KalyanaVaran.Api.exe: run through the Microsoft-signed dotnet.exe instead.
         Avoids "An Application Control policy has blocked this file" (Smart App Control / AppLocker). -->
    <UseAppHost>false</UseAppHost>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.0" />
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Scalar.AspNetCore" Version="2.*" />
    <!-- SMTP email (Gmail, Amazon SES SMTP, Office 365 …) -->
    <PackageReference Include="MailKit" Version="4.*" />
  </ItemGroup>

  <!-- Security pins for packages pulled in indirectly (NU1902 / NU1903 warnings):
       Microsoft.OpenApi 2.0.0 (via Microsoft.AspNetCore.OpenApi)  -> GHSA-v5pm-xwqc-g5wc, fixed in 2.7.5
       System.Security.Cryptography.Pkcs 9.0.4 (via Microsoft.Data.SqlClient) -> GHSA-846f-f764-76mx / GHSA-2fmv-rv2c-59x7 -->
  <ItemGroup>
    <PackageReference Include="Microsoft.OpenApi" Version="2.7.5" />
    <PackageReference Include="System.Security.Cryptography.Pkcs" Version="10.0.13" />
  </ItemGroup>

  <ItemGroup>
    <Folder Include="wwwroot\uploads\" />
  </ItemGroup>

</Project>
```
---

## Step 3: REPLACE the settings files

### 3a. `appsettings.json`
This file holds **no secrets**; keys go into user-secrets in step 9.

```json
{
  "ConnectionStrings": {
    "Default": "Server=tcp:aspireright.database.windows.net,1433;Initial Catalog=KalyanaVaram;User ID=AspireAdmin;Password=SET_IN_USER_SECRETS;Encrypt=True;TrustServerCertificate=False;MultipleActiveResultSets=True;Connection Timeout=30"
  },
  "Jwt": {
    "Issuer": "kalyanavaran-api",
    "Audience": "kalyanavaran-web",
    "SigningKey": "CHANGE_ME_this_must_be_a_long_random_secret_of_at_least_32_chars",
    "AccessTokenMinutes": 60,
    "RefreshTokenDays": 30
  },
  "Otp": {
    "ExpirySeconds": 600,
    "ResendAfterSeconds": 30,
    "MaxSendsPerHour": 5,
    "MaxVerifyAttempts": 5,
    "HashSecret": "CHANGE_ME_otp_hash_secret",
    "DevFixedCode": null
  },
  "Sms": {
    "Provider": "Fast2Sms",
    "ApiKey": null,
    "ApiSecret": null,
    "SenderId": null,
    "TemplateId": null,
    "OtpVariable": "otp",
    "ExpiryVariable": null,
    "WhatsAppFrom": null
  },
  "Email": {
    "Provider": "Smtp",
    "Host": "smtp.gmail.com",
    "Port": 587,
    "Username": null,
    "Password": null,
    "From": null,
    "FromName": "KalyanaVaran Matrimony",
    "ApiKey": null
  },
  "Storage": {
    "PublicRoot": "wwwroot/uploads",
    "PrivateRoot": "private-uploads",
    "PublicBaseUrl": null,
    "MaxPhotoBytes": 5242880,
    "MaxPhotos": 5,
    "MaxKycFileBytes": 10485760,
    "MaxVideoBytes": 52428800
  },
  "RateLimits": {
    "GlobalPerMinute": 300,
    "AuthPerMinute": 10,
    "UploadsPerMinute": 20
  },
  "Cors": {
    "Origins": [
      "http://localhost:3000",
      "https://divine-matrimony-web.web.app"
    ]
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```
> Keep your own `ConnectionStrings`, `Jwt` and `Cors` values if you changed them earlier.

### 3b. `appsettings.Development.json`
On your PC, SMS and email stay in **fake (Console) mode** with code `842910` until you add real keys.

```json
{
  "Otp": {
    "DevFixedCode": "842910"
  },
  "Sms": {
    "Provider": "Console"
  },
  "Email": {
    "Provider": "Console"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```
---

## Step 4: NEW folder `Services/Delivery` with 9 files
In Explorer, right-click **Services** → *New Folder* → `Delivery`. Then create each file below inside it.

### 4.1 `Services/Delivery/DeliveryCore.cs`
Settings classes, sender interfaces, error type, phone/email masking for logs.

```csharp
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
```
### 4.2 `Services/Delivery/ConsoleSenders.cs`
Fake senders for Development only (they send nothing and never log the code).

```csharp
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
```
### 4.3 `Services/Delivery/Fast2SmsSender.cs`
Fast2SMS: the cheapest SMS option in India.

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KalyanaVaran.Api.Services.Delivery;

/// <summary>
/// Fast2SMS (India, low cost) bulkV2 API:
///   POST https://www.fast2sms.com/dev/bulkV2   header authorization: {Sms:ApiKey}
///   DLT route (recommended): { "route": "dlt", "sender_id": "KVMTRY", "message": "{DLT message id}",
///                               "variables_values": "123456|10", "numbers": "9876543210" }
///   OTP route:               { "route": "otp", "variables_values": "123456", "numbers": "9876543210" }
/// Sms:SenderId = approved DLT header, Sms:TemplateId = Fast2SMS DLT "Message ID" of the approved OTP template.
/// Leave Sms:TemplateId empty to use the "otp" route. Check the field names in your Fast2SMS dashboard → Dev API.
/// </summary>
public sealed class Fast2SmsSender(HttpClient http, IOptions<SmsOptions> options, ILogger<Fast2SmsSender> logger) : ISmsSender
{
    public bool SupportsWhatsApp => false;

    public async Task<string?> SendOtpAsync(string mobile, string channel, string code, int validMinutes, CancellationToken ct)
    {
        var o = options.Value;
        var number = mobile.StartsWith("+91") ? mobile[3..] : mobile.TrimStart('+');   // 10 digits

        Dictionary<string, string> payload = string.IsNullOrWhiteSpace(o.TemplateId)
            ? new() { ["route"] = "otp", ["variables_values"] = code, ["numbers"] = number }
            : new()
            {
                ["route"] = "dlt",
                ["sender_id"] = o.SenderId ?? "",
                ["message"] = o.TemplateId,
                // Template variables in order, separated by "|": {#var#} = code, then validity minutes if the template has it
                ["variables_values"] = string.IsNullOrWhiteSpace(o.ExpiryVariable) ? code : $"{code}|{validMinutes}",
                ["numbers"] = number,
                ["flash"] = "0"
            };

        using var req = new HttpRequestMessage(HttpMethod.Post, "dev/bulkV2") { Content = JsonContent.Create(payload) };
        req.Headers.TryAddWithoutValidation("authorization", o.ApiKey);
        req.Headers.Accept.ParseAdd("application/json");

        HttpResponseMessage res;
        try
        {
            res = await http.SendAsync(req, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new OtpDeliveryException("Fast2SMS is unreachable.", providerUnavailable: true, ex);
        }

        using (res)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            bool ok = false;
            string? requestId = null, message = null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                ok = root.TryGetProperty("return", out var r) && r.ValueKind == JsonValueKind.True;
                requestId = root.TryGetProperty("request_id", out var id) ? id.ToString() : null;
                message = root.TryGetProperty("message", out var m) ? m.ToString() : null;
            }
            catch (JsonException) { /* non-JSON */ }

            if (res.IsSuccessStatusCode && ok) return requestId;

            var status = (int)res.StatusCode;
            logger.LogWarning("Fast2SMS rejected SMS to {Mobile}: HTTP {Status} {Body}", OtpText.Mask(mobile), status, OtpText.Truncate(body));
            throw new OtpDeliveryException($"Fast2SMS error (HTTP {status}): {OtpText.Truncate(message, 150)}",
                providerUnavailable: status is 401 or 403 or >= 500);
        }
    }
}
```
### 4.4 `Services/Delivery/Msg91SmsSender.cs`
MSG91 SMS.

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KalyanaVaran.Api.Services.Delivery;

/// <summary>
/// MSG91 Flow API (India, DLT-compliant). Sends OUR generated code through an approved DLT template:
///   POST https://control.msg91.com/api/v5/flow   header authkey: {Sms:ApiKey}
///   { "template_id": "...", "short_url": "0", "recipients": [ { "mobiles": "919876543210", "otp": "123456" } ] }
/// The template text (registered on DLT + MSG91) must contain the variable, e.g.
///   "##otp## is your KalyanaVaran Matrimony verification code. Valid for ##minutes## minutes. Do not share it with anyone."
/// </summary>
public sealed class Msg91SmsSender(HttpClient http, IOptions<SmsOptions> options, ILogger<Msg91SmsSender> logger) : ISmsSender
{
    public bool SupportsWhatsApp => false;

    public async Task<string?> SendOtpAsync(string mobile, string channel, string code, int validMinutes, CancellationToken ct)
    {
        var o = options.Value;
        var recipient = new Dictionary<string, string>
        {
            ["mobiles"] = mobile.TrimStart('+'),   // MSG91 expects 91XXXXXXXXXX
            [o.OtpVariable] = code
        };
        if (!string.IsNullOrWhiteSpace(o.ExpiryVariable)) recipient[o.ExpiryVariable] = validMinutes.ToString();

        var payload = new Dictionary<string, object>
        {
            ["template_id"] = o.TemplateId!,
            ["short_url"] = "0",
            ["recipients"] = new[] { recipient }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "api/v5/flow") { Content = JsonContent.Create(payload) };
        req.Headers.Add("authkey", o.ApiKey);
        req.Headers.Accept.ParseAdd("application/json");

        HttpResponseMessage res;
        try
        {
            res = await http.SendAsync(req, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new OtpDeliveryException("MSG91 is unreachable.", providerUnavailable: true, ex);
        }

        using (res)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            string? type = null, message = null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
                message = doc.RootElement.TryGetProperty("message", out var m) ? m.ToString() : null;
            }
            catch (JsonException) { /* non-JSON error page */ }

            if (res.IsSuccessStatusCode && string.Equals(type, "success", StringComparison.OrdinalIgnoreCase))
                return message;   // MSG91 request id

            var status = (int)res.StatusCode;
            logger.LogWarning("MSG91 rejected SMS to {Mobile}: HTTP {Status} {Body}", OtpText.Mask(mobile), status, OtpText.Truncate(body));
            // 401/403 = bad auth key, 5xx = MSG91 down -> treat as provider unavailable
            throw new OtpDeliveryException($"MSG91 error (HTTP {status}): {OtpText.Truncate(message, 150)}",
                providerUnavailable: status is 401 or 403 or >= 500);
        }
    }
}
```
### 4.5 `Services/Delivery/TwilioSmsSender.cs`
Twilio SMS / WhatsApp.

```csharp
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KalyanaVaran.Api.Services.Delivery;

/// <summary>
/// Twilio Programmable Messaging (SMS + optional WhatsApp):
///   POST https://api.twilio.com/2010-04-01/Accounts/{AccountSid}/Messages.json   (Basic auth AccountSid:AuthToken)
///   form: To, Body, From (or MessagingServiceSid)
/// Sms:ApiKey = Account SID, Sms:ApiSecret = Auth Token, Sms:SenderId = +1… number or MG… Messaging Service SID.
/// </summary>
public sealed class TwilioSmsSender(HttpClient http, IOptions<SmsOptions> options, ILogger<TwilioSmsSender> logger) : ISmsSender
{
    public bool SupportsWhatsApp => !string.IsNullOrWhiteSpace(options.Value.WhatsAppFrom);

    public async Task<string?> SendOtpAsync(string mobile, string channel, string code, int validMinutes, CancellationToken ct)
    {
        var o = options.Value;
        var whatsapp = channel == "whatsapp";

        var form = new Dictionary<string, string>
        {
            ["To"] = whatsapp ? $"whatsapp:{mobile}" : mobile,
            ["Body"] = OtpText.Sms(code, validMinutes)
        };
        if (whatsapp) form["From"] = $"whatsapp:{o.WhatsAppFrom}";
        else if (o.SenderId!.StartsWith("MG", StringComparison.Ordinal)) form["MessagingServiceSid"] = o.SenderId;
        else form["From"] = o.SenderId;

        using var req = new HttpRequestMessage(HttpMethod.Post, $"2010-04-01/Accounts/{o.ApiKey}/Messages.json")
        {
            Content = new FormUrlEncodedContent(form)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{o.ApiKey}:{o.ApiSecret}")));

        HttpResponseMessage res;
        try
        {
            res = await http.SendAsync(req, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new OtpDeliveryException("Twilio is unreachable.", providerUnavailable: true, ex);
        }

        using (res)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            string? sid = null, errMessage = null, errCode = null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                sid = doc.RootElement.TryGetProperty("sid", out var s) ? s.GetString() : null;
                errMessage = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
                errCode = doc.RootElement.TryGetProperty("code", out var c) ? c.ToString() : null;
            }
            catch (JsonException) { /* non-JSON */ }

            if (res.IsSuccessStatusCode && sid is not null) return sid;

            var status = (int)res.StatusCode;
            logger.LogWarning("Twilio rejected {Channel} to {Mobile}: HTTP {Status} code {Code} {Message}",
                channel, OtpText.Mask(mobile), status, errCode, OtpText.Truncate(errMessage, 200));
            throw new OtpDeliveryException($"Twilio error {errCode} (HTTP {status}): {OtpText.Truncate(errMessage, 150)}",
                providerUnavailable: status is 401 or 403 or >= 500);
        }
    }
}
```
### 4.6 `Services/Delivery/OtpEmailTemplate.cs`
Professional HTML + text OTP email.

```csharp
using System.Net;

namespace KalyanaVaran.Api.Services.Delivery;

/// <summary>Professional OTP email (HTML + plain-text). Inline styles so it renders in Gmail / Outlook.</summary>
public static class OtpEmailTemplate
{
    public const string Subject = "Your KalyanaVaran Matrimony verification code";

    public static string Text(string? name, string code, int minutes) =>
$@"Hello {(string.IsNullOrWhiteSpace(name) ? "there" : name)},

Your KalyanaVaran Matrimony verification code is: {code}

This code is valid for {minutes} minutes and can be used only once.
Never share this code with anyone. Our team will never ask for it.

If you did not request this code, you can safely ignore this email.

- KalyanaVaran Matrimony";

    public static string Html(string? name, string code, int minutes)
    {
        var who = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(name) ? "there" : name);
        var digits = string.Concat(code.Select(c =>
            $"<td style=\"width:44px;height:54px;border:1px solid #e5d3b3;border-radius:10px;background:#fffaf0;" +
            $"font:700 26px/54px Arial,Helvetica,sans-serif;color:#7a1f2b;text-align:center\">{c}</td><td style=\"width:8px\"></td>"));

        return $@"<!doctype html>
<html><body style=""margin:0;padding:0;background:#f6f1ea"">
<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f6f1ea;padding:32px 12px"">
<tr><td align=""center"">
  <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""max-width:520px;background:#ffffff;border-radius:16px;overflow:hidden;font-family:Arial,Helvetica,sans-serif"">
    <tr><td style=""background:#7a1f2b;padding:22px 28px;color:#ffffff;font-size:20px;font-weight:700"">KalyanaVaran Matrimony</td></tr>
    <tr><td style=""padding:28px 28px 8px;color:#1f2937;font-size:16px;line-height:24px"">
      Hello {who},<br><br>Use this code to verify your email address:
    </td></tr>
    <tr><td style=""padding:12px 28px"" align=""left"">
      <table role=""presentation"" cellpadding=""0"" cellspacing=""0""><tr>{digits}</tr></table>
    </td></tr>
    <tr><td style=""padding:8px 28px 4px;color:#374151;font-size:15px;line-height:22px"">
      This code is valid for <b>{minutes} minutes</b> and can be used only once.
    </td></tr>
    <tr><td style=""padding:12px 28px 24px"">
      <div style=""background:#fef3c7;border-radius:10px;padding:12px 14px;color:#92400e;font-size:13px;line-height:20px"">
        Never share this code with anyone. KalyanaVaran staff will never ask for it.
      </div>
    </td></tr>
    <tr><td style=""padding:0 28px 28px;color:#6b7280;font-size:13px;line-height:20px"">
      If you did not request this code, you can safely ignore this email; your account is safe.
    </td></tr>
    <tr><td style=""background:#faf7f2;padding:16px 28px;color:#9ca3af;font-size:12px"">
      © KalyanaVaran Matrimony · This is an automated message, please do not reply.
    </td></tr>
  </table>
</td></tr></table>
</body></html>";
    }
}
```
### 4.7 `Services/Delivery/SmtpEmailSender.cs`
SMTP email: Gmail, Brevo, Amazon SES.

```csharp
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace KalyanaVaran.Api.Services.Delivery;

/// <summary>
/// SMTP via MailKit (Gmail, Amazon SES SMTP, Zoho, Office 365 …).
/// Port 465 = implicit TLS, otherwise STARTTLS (587).
/// Gmail: smtp.gmail.com:587 + a 16-character App Password (needs 2-Step Verification).
/// </summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task<string?> SendOtpAsync(string email, string? name, string code, int validMinutes, CancellationToken ct)
    {
        var o = options.Value;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(o.FromName, o.From));
        message.To.Add(new MailboxAddress(name ?? "", email));
        message.Subject = OtpEmailTemplate.Subject;
        message.Body = new BodyBuilder
        {
            TextBody = OtpEmailTemplate.Text(name, code, validMinutes),
            HtmlBody = OtpEmailTemplate.Html(name, code, validMinutes)
        }.ToMessageBody();

        using var client = new SmtpClient { Timeout = 20_000 };
        try
        {
            var security = o.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
            await client.ConnectAsync(o.Host, o.Port, security, ct);
            if (!string.IsNullOrEmpty(o.Username))
                await client.AuthenticateAsync(o.Username, o.Password ?? "", ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
            return message.MessageId;
        }
        catch (AuthenticationException ex)
        {
            logger.LogError("SMTP login failed for {User} on {Host}: {Error}", o.Username, o.Host, ex.Message);
            throw new OtpDeliveryException("Email service login failed.", providerUnavailable: true, ex);
        }
        catch (SmtpCommandException ex) when (ex.ErrorCode is SmtpErrorCode.RecipientNotAccepted)
        {
            logger.LogWarning("SMTP rejected recipient {Email}: {Error}", OtpText.Mask(email), ex.Message);
            throw new OtpDeliveryException("The email address was rejected.", providerUnavailable: false, ex);
        }
        catch (SmtpCommandException ex)
        {
            logger.LogWarning("SMTP command failed ({Status}) for {Email}: {Error}", ex.StatusCode, OtpText.Mask(email), ex.Message);
            throw new OtpDeliveryException("The email server refused the message.", providerUnavailable: (int)ex.StatusCode >= 400 && (int)ex.StatusCode < 500, ex);
        }
        catch (Exception ex) when (ex is not OtpDeliveryException && !(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            logger.LogError("SMTP server {Host}:{Port} unreachable: {Error}", o.Host, o.Port, ex.Message);
            throw new OtpDeliveryException("Email server is unreachable.", providerUnavailable: true, ex);
        }
    }
}
```
### 4.8 `Services/Delivery/SendGridEmailSender.cs`
SendGrid email (paid only).

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace KalyanaVaran.Api.Services.Delivery;

/// <summary>
/// SendGrid v3 Mail Send API (no SDK needed):
///   POST https://api.sendgrid.com/v3/mail/send   Authorization: Bearer {Email:ApiKey}   → 202 Accepted
/// Email:From must be a verified sender / authenticated domain in SendGrid.
/// </summary>
public sealed class SendGridEmailSender(HttpClient http, IOptions<EmailOptions> options, ILogger<SendGridEmailSender> logger) : IEmailSender
{
    public async Task<string?> SendOtpAsync(string email, string? name, string code, int validMinutes, CancellationToken ct)
    {
        var o = options.Value;
        var payload = new
        {
            personalizations = new[] { new { to = new[] { new { email, name = string.IsNullOrWhiteSpace(name) ? email : name } } } },
            from = new { email = o.From, name = o.FromName },
            subject = OtpEmailTemplate.Subject,
            content = new[]
            {
                new { type = "text/plain", value = OtpEmailTemplate.Text(name, code, validMinutes) },
                new { type = "text/html", value = OtpEmailTemplate.Html(name, code, validMinutes) }
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "v3/mail/send") { Content = JsonContent.Create(payload) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiKey);

        HttpResponseMessage res;
        try
        {
            res = await http.SendAsync(req, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new OtpDeliveryException("SendGrid is unreachable.", providerUnavailable: true, ex);
        }

        using (res)
        {
            if (res.IsSuccessStatusCode)
                return res.Headers.TryGetValues("X-Message-Id", out var ids) ? ids.FirstOrDefault() : null;

            var status = (int)res.StatusCode;
            var body = await res.Content.ReadAsStringAsync(ct);
            logger.LogWarning("SendGrid rejected email to {Email}: HTTP {Status} {Body}", OtpText.Mask(email), status, OtpText.Truncate(body));
            throw new OtpDeliveryException($"SendGrid error (HTTP {status}).", providerUnavailable: status is 401 or 403 or 429 or >= 500);
        }
    }
}
```
### 4.9 `Services/Delivery/OtpDeliveryRegistration.cs`
Picks the provider from settings, reads env vars, checks settings at startup.

```csharp
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

    /// <summary>Registers the configured SMS + email senders and validates their settings at startup.</summary>
    public static IServiceCollection AddOtpDelivery(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        services.Configure<SmsOptions>(config.GetSection("Sms"));
        services.Configure<EmailOptions>(config.GetSection("Email"));

        var sms = config.GetSection("Sms").Get<SmsOptions>() ?? new SmsOptions();
        var email = config.GetSection("Email").Get<EmailOptions>() ?? new EmailOptions();
        var otp = config.GetSection("Otp").Get<OtpOptions>() ?? new OtpOptions();

        Validate(sms, email, otp, env);

        switch (sms.Provider.Trim().ToLowerInvariant())
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

        switch (email.Provider.Trim().ToLowerInvariant())
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

    private static void Validate(SmsOptions sms, EmailOptions email, OtpOptions otp, IHostEnvironment env)
    {
        var errors = new List<string>();
        var smsProvider = sms.Provider.Trim().ToLowerInvariant();
        var emailProvider = email.Provider.Trim().ToLowerInvariant();

        void Need(string? value, string configKey, string envVar)
        {
            if (string.IsNullOrWhiteSpace(value)) errors.Add($"{configKey} (env {envVar})");
        }

        switch (smsProvider)
        {
            case "msg91":
                Need(sms.ApiKey, "Sms:ApiKey = MSG91 AuthKey", "SMS_API_KEY");
                Need(sms.TemplateId, "Sms:TemplateId = MSG91 Flow/DLT template id", "SMS_TEMPLATE_ID");
                break;
            case "fast2sms":
                Need(sms.ApiKey, "Sms:ApiKey = Fast2SMS API key", "SMS_API_KEY");
                if (!string.IsNullOrWhiteSpace(sms.TemplateId))
                    Need(sms.SenderId, "Sms:SenderId = approved DLT sender id (header)", "SMS_SENDER_ID");
                break;
            case "twilio":
                Need(sms.ApiKey, "Sms:ApiKey = Twilio Account SID", "SMS_API_KEY");
                Need(sms.ApiSecret, "Sms:ApiSecret = Twilio Auth Token", "SMS_API_SECRET");
                Need(sms.SenderId, "Sms:SenderId = Twilio number or Messaging Service SID", "SMS_SENDER_ID");
                break;
            case "console":
                break;
            default:
                errors.Add($"Sms:Provider '{sms.Provider}' is unknown. Use Fast2Sms, Msg91, Twilio or Console");
                break;
        }

        switch (emailProvider)
        {
            case "smtp":
                Need(email.Host, "Email:Host", "EMAIL_HOST");
                Need(email.From, "Email:From", "EMAIL_FROM");
                if (!string.IsNullOrWhiteSpace(email.Username)) Need(email.Password, "Email:Password", "EMAIL_PASSWORD");
                break;
            case "sendgrid":
                Need(email.ApiKey, "Email:ApiKey = SendGrid API key", "EMAIL_API_KEY");
                Need(email.From, "Email:From (verified sender)", "EMAIL_FROM");
                break;
            case "console":
                break;
            default:
                errors.Add($"Email:Provider '{email.Provider}' is unknown. Use Smtp, SendGrid or Console");
                break;
        }

        var usesConsole = smsProvider == "console" || emailProvider == "console";
        if (!env.IsDevelopment())
        {
            if (usesConsole)
                errors.Add("Console OTP providers send nothing and are allowed only in Development. Configure a real Sms:Provider and Email:Provider");
            if (!string.IsNullOrWhiteSpace(otp.DevFixedCode))
                errors.Add("Otp:DevFixedCode must not be set outside Development");
        }
        else if (usesConsole && string.IsNullOrWhiteSpace(otp.DevFixedCode))
        {
            errors.Add("Console providers never print the OTP. Set Otp:DevFixedCode (e.g. 842910) in appsettings.Development.json to test without real SMS/email");
        }

        if (errors.Count > 0)
            throw new InvalidOperationException("OTP delivery is not configured correctly:\n - " + string.Join("\n - ", errors) +
                                                "\nSee docs/OTP_DELIVERY.md.");
    }
}
```
> Check: `Services/Delivery` now has 9 files.

---

## Step 5: REPLACE the services

### 5a. `Services/OtpService.cs`
The old `ISmsSender`, `IEmailSender`, `ConsoleSmsSender` and `ConsoleEmailSender` classes are **removed from this file**; they now live in `Services/Delivery`. If you keep the old classes here, the build fails with "duplicate definition".

```csharp
using System.Security.Cryptography;
using System.Text;
using KalyanaVaran.Api.Common;
using KalyanaVaran.Api.Contracts;
using KalyanaVaran.Api.Data;
using KalyanaVaran.Api.Entities;
using KalyanaVaran.Api.Services.Delivery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KalyanaVaran.Api.Services;

public sealed record OtpVerifyResult(long UserId, bool EmailVerified);

/// <summary>
/// Generates, delivers (real SMS / email via Services/Delivery) and verifies OTPs.
/// - 6-digit code from a CSPRNG, stored only as HMAC-SHA256(secret, id:code)
/// - expiry, resend cooldown, max sends per hour, max wrong attempts, single use
/// - phone (sms / whatsapp) and email codes are separate rows with separate limits;
///   only the newest unused code of each kind is valid
/// - the code is never logged or returned
/// </summary>
public sealed class OtpService(
    AppDbContext db,
    ISmsSender sms,
    IEmailSender email,
    IOptions<OtpOptions> options,
    IOptions<SmsOptions> smsOptions,
    IOptions<EmailOptions> emailOptions,
    IHostEnvironment env,
    ILogger<OtpService> logger)
{
    public const string Email = "email";
    private readonly OtpOptions _o = options.Value;

    private int ValidMinutes => (int)Math.Ceiling(_o.ExpirySeconds / 60.0);

    public async Task<OtpSentResponse> SendAsync(User user, string channel, string purpose, HttpContext http, CancellationToken ct)
    {
        var mobile = user.Mobile ?? throw ApiException.Validation("This account has no mobile number.", "mobile");
        var isEmail = channel == Email;
        var destination = isEmail
            ? user.Email ?? throw ApiException.Validation("This account has no email address.", "email")
            : mobile;

        if (channel == "whatsapp" && !sms.SupportsWhatsApp)
            throw ApiException.Validation("WhatsApp OTP is not available yet. Please use SMS.", "channel");

        var now = DateTime.UtcNow;
        var hourAgo = now.AddHours(-1);

        // ---- 1. Cooldown + hourly limit (checked BEFORE anything is sent) ----
        var recent = await SameKind(db.OtpRequests.Where(o => o.Mobile == mobile && o.CreatedAt > hourAgo), isEmail)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);

        var latest = recent.FirstOrDefault();
        if (latest is not null && latest.ResendAfter > now)
        {
            var wait = (int)Math.Ceiling((latest.ResendAfter - now).TotalSeconds);
            throw ApiException.OtpRateLimited($"Please wait {wait} seconds before requesting a new OTP.");
        }
        if (recent.Count >= _o.MaxSendsPerHour)
            throw ApiException.OtpRateLimited("Too many OTP requests. Please try again after some time.");

        // ---- 2. Generate ----
        var id = "otp_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        var code = UseDevFixedCode(isEmail)
            ? _o.DevFixedCode!
            : RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        // ---- 3. Deliver first: if the provider fails, nothing is stored and the old code stays valid ----
        string? providerMessageId;
        try
        {
            providerMessageId = isEmail
                ? await email.SendOtpAsync(destination, user.FullName, code, ValidMinutes, ct)
                : await sms.SendOtpAsync(mobile, channel, code, ValidMinutes, ct);
        }
        catch (OtpDeliveryException ex)
        {
            logger.LogWarning(ex, "OTP delivery failed: channel={Channel} to={Destination} providerDown={Down}",
                channel, OtpText.Mask(destination), ex.ProviderUnavailable);
            throw DeliveryError(isEmail, ex.ProviderUnavailable);
        }

        // ---- 4. Store hash; only the newest code of this kind stays valid ----
        foreach (var old in recent.Where(o => o.ConsumedAt is null)) old.ConsumedAt = now;

        db.OtpRequests.Add(new OtpRequest
        {
            Id = id,
            UserId = user.Id,
            Mobile = mobile,
            Channel = channel,
            Destination = destination,
            Purpose = purpose,
            OtpHash = Hash(id, code),
            ExpiresAt = now.AddSeconds(_o.ExpirySeconds),
            ResendAfter = now.AddSeconds(_o.ResendAfterSeconds),
            ProviderMessageId = providerMessageId is null ? null : OtpText.Truncate(providerMessageId, 100),
            Ip = http.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = now
        });
        await db.SaveChangesAsync(ct);

        logger.LogInformation("OTP sent: id={OtpId} channel={Channel} to={Destination} purpose={Purpose}",
            id, channel, OtpText.Mask(destination), purpose);

        return new OtpSentResponse(id, mobile, channel, _o.ExpirySeconds, _o.ResendAfterSeconds);
    }

    /// <summary>
    /// Checks the mobile code (required) and the email code (optional). Both are checked before
    /// either is consumed. The newest active code for the mobile is used, so an old otpRequestId
    /// after "Resend OTP" still works with the newest code.
    /// </summary>
    public async Task<OtpVerifyResult> VerifyAsync(string otpRequestId, string mobile, string code, string? emailCode,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Same request verified before → explicit "already used"
        var requested = await db.OtpRequests.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == otpRequestId && o.Mobile == mobile, ct);
        if (requested?.VerifiedAt is not null)
            throw FieldError(400, ErrorCodes.OtpAlreadyUsed, "This OTP has already been used. Please request a new one.", "otp");

        var phone = await ActiveAsync(mobile, isEmail: false, ct);
        if (phone is null)
            throw requested is not null
                ? ApiException.OtpExpired("This OTP is no longer valid. Please request a new one.")
                : ApiException.OtpInvalid("This OTP request is not valid. Please request a new OTP.");
        EnsureUsable(phone, now, "otp", "OTP");

        OtpRequest? mail = null;
        if (!string.IsNullOrWhiteSpace(emailCode))
        {
            mail = await ActiveAsync(mobile, isEmail: true, ct)
                   ?? throw FieldError(400, ErrorCodes.OtpExpired, "The email OTP has expired. Please request a new one.", "emailOtp");
            EnsureUsable(mail, now, "emailOtp", "email OTP");
        }

        if (!Matches(phone, code)) await FailAsync(phone, "otp", "mobile OTP", ct);
        if (mail is not null && !Matches(mail, emailCode!)) await FailAsync(mail, "emailOtp", "email OTP", ct);

        // Single use: consumed + verified
        phone.ConsumedAt = now;
        phone.VerifiedAt = now;
        if (mail is not null)
        {
            mail.ConsumedAt = now;
            mail.VerifiedAt = now;
        }
        await db.SaveChangesAsync(ct);

        return new OtpVerifyResult(phone.UserId, mail is not null);
    }

    /// <summary>The fixed code is used ONLY in Development AND only for a channel whose provider is Console.</summary>
    private bool UseDevFixedCode(bool isEmail)
    {
        if (!env.IsDevelopment() || string.IsNullOrWhiteSpace(_o.DevFixedCode)) return false;
        var provider = isEmail ? emailOptions.Value.Provider : smsOptions.Value.Provider;
        return provider.Equals("Console", StringComparison.OrdinalIgnoreCase);
    }

    private static ApiException DeliveryError(bool isEmail, bool providerDown) => isEmail
        ? new ApiException(providerDown ? 503 : 502, ErrorCodes.OtpDeliveryFailed,
            providerDown
                ? "Our email service is temporarily unavailable. Please try again in a few minutes or use the mobile OTP."
                : "We couldn't send the OTP to your email address. Please check the email and try again.",
            new Dictionary<string, string> { ["email"] = "OTP email could not be delivered." })
        : new ApiException(providerDown ? 503 : 502, ErrorCodes.OtpDeliveryFailed,
            providerDown
                ? "Our SMS service is temporarily unavailable. Please try again in a few minutes."
                : "We couldn't send the OTP to this mobile number. Please check the number and try again.",
            new Dictionary<string, string> { ["mobile"] = "OTP SMS could not be delivered." });

    private Task<OtpRequest?> ActiveAsync(string mobile, bool isEmail, CancellationToken ct) =>
        SameKind(db.OtpRequests.Where(o => o.Mobile == mobile && o.ConsumedAt == null), isEmail)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(ct);

    private static IQueryable<OtpRequest> SameKind(IQueryable<OtpRequest> q, bool isEmail) =>
        isEmail ? q.Where(o => o.Channel == Email) : q.Where(o => o.Channel != Email);

    private void EnsureUsable(OtpRequest otp, DateTime now, string field, string label)
    {
        if (otp.Attempts >= _o.MaxVerifyAttempts)
            throw FieldError(429, ErrorCodes.OtpRateLimited, $"Too many wrong attempts. Please request a new {label}.", field);
        if (otp.ExpiresAt <= now)
            throw FieldError(400, ErrorCodes.OtpExpired, $"This {label} has expired. Please request a new one.", field);
    }

    private async Task FailAsync(OtpRequest otp, string field, string label, CancellationToken ct)
    {
        otp.Attempts++;
        await db.SaveChangesAsync(ct);
        var left = _o.MaxVerifyAttempts - otp.Attempts;
        throw left > 0
            ? FieldError(400, ErrorCodes.OtpInvalid,
                $"The {label} you entered is incorrect. {left} attempt{(left == 1 ? "" : "s")} left.", field)
            : FieldError(429, ErrorCodes.OtpRateLimited, $"Too many wrong attempts. Please request a new {label}.", field);
    }

    private bool Matches(OtpRequest otp, string code) =>
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(otp.OtpHash), Convert.FromHexString(Hash(otp.Id, code)));

    private static ApiException FieldError(int status, string code, string message, string field) =>
        new(status, code, message, new Dictionary<string, string> { [field] = message });

    private string Hash(string otpId, string code) =>
        Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(_o.HashSecret), Encoding.UTF8.GetBytes($"{otpId}:{code}")));
}
```
### 5b. `Services/AuthService.cs`
What changed: it now takes a logger, and if the email OTP fails at registration, registration still succeeds on the SMS OTP.

```csharp
using System.Security.Cryptography;
using KalyanaVaran.Api.Common;
using KalyanaVaran.Api.Contracts;
using KalyanaVaran.Api.Data;
using KalyanaVaran.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace KalyanaVaran.Api.Services;

public sealed class AuthService(AppDbContext db, OtpService otp, TokenService tokens, ILogger<AuthService> logger)
{
    // A1
    public async Task<OtpSentResponse> RegisterAsync(RegisterRequest req, HttpContext http, CancellationToken ct)
    {
        var mobile = req.Mobile!.Trim();
        var email = req.Email!.Trim().ToLowerInvariant();

        var existing = await db.Users.Where(u => u.Mobile == mobile || u.Email == email).ToListAsync(ct);

        var activeByMobile = existing.FirstOrDefault(u => u.Mobile == mobile && u.Status != UserStatus.Pending);
        if (activeByMobile is not null)
            throw ApiException.Conflict("This mobile number is already registered. Please log in.", "mobile");

        var activeByEmail = existing.FirstOrDefault(u => u.Email == email && u.Status != UserStatus.Pending);
        if (activeByEmail is not null)
            throw ApiException.Conflict("This email is already registered. Please log in.", "email");

        // Abandoned registrations (never verified): reuse the one with this mobile (keeps its OTP
        // history so rate limits still apply) and drop a different pending row holding this email.
        var user = existing.FirstOrDefault(u => u.Mobile == mobile);
        var staleByEmail = existing.Where(u => u != user).ToList();
        if (staleByEmail.Count > 0)
        {
            db.Users.RemoveRange(staleByEmail);
            await db.SaveChangesAsync(ct);
        }

        var now = DateTime.UtcNow;
        if (user is null)
        {
            user = new User { MemberId = await NewMemberIdAsync(ct), Mobile = mobile, CreatedAt = now };
            db.Users.Add(user);
        }
        user.ProfileFor = req.ProfileFor!;
        user.FullName = req.FullName!.Trim();
        user.Gender = req.Gender!;
        user.Email = email;
        user.Status = UserStatus.Pending;
        user.TermsAcceptedAt = now;
        user.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        // The OTP screen shows "Sent via SMS & email": send both codes.
        // SMS is required (a failure is returned to the app); email is best-effort.
        var sent = await otp.SendAsync(user, "sms", "register", http, ct);
        try
        {
            await otp.SendAsync(user, OtpService.Email, "register", http, ct);
        }
        catch (ApiException ex) when (ex.Code is ErrorCodes.OtpRateLimited or ErrorCodes.OtpDeliveryFailed)
        {
            // SMS already went out; the user can still verify with the mobile OTP and
            // resend the email code later with POST /v1/auth/otp/resend { channel: "email" }.
            logger.LogWarning("Register {MemberId}: email OTP not sent ({Code}); continuing with SMS OTP.", user.MemberId, ex.Code);
        }
        return sent;
    }

    // A2
    public async Task<OtpSentResponse> SendOtpAsync(SendOtpRequest req, HttpContext http, CancellationToken ct)
    {
        var mobile = req.Mobile!.Trim();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Mobile == mobile, ct)
                   ?? throw ApiException.NotFound("No account found for this mobile number. Please register first.");
        if (user.Status == UserStatus.Blocked)
            throw ApiException.Forbidden("This account has been blocked. Please contact support.");

        var purpose = user.Status == UserStatus.Pending ? "register" : "login";
        return await otp.SendAsync(user, req.Channel, purpose, http, ct);
    }

    // A3
    public async Task<AuthSession> VerifyOtpAsync(VerifyOtpRequest req, HttpContext http, CancellationToken ct)
    {
        var result = await otp.VerifyAsync(req.OtpRequestId!, req.Mobile!.Trim(), req.Otp!, req.EmailOtp, ct);

        var user = await db.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.Id == result.UserId, ct)
                   ?? throw ApiException.NotFound("Account not found.");
        if (user.Status == UserStatus.Blocked)
            throw ApiException.Forbidden("This account has been blocked. Please contact support.");

        var now = DateTime.UtcNow;
        var isNewUser = user.Status == UserStatus.Pending;
        if (isNewUser)
        {
            user.Status = UserStatus.Active;
            user.MobileVerified = true;
        }
        if (result.EmailVerified) user.EmailVerified = true;
        if (user.Profile is null)
        {
            var (first, last) = ProfileParsing.SplitName(user.FullName);
            db.Profiles.Add(new Profile { UserId = user.Id, FirstName = first, LastName = last, UpdatedAt = now });
        }
        user.LastLoginAt = now;
        user.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        var authTokens = await tokens.IssueAsync(user, http, ct);
        return new AuthSession(authTokens, ToAuthUser(user, isNewUser));
    }

    // A5
    public Task<AuthTokens> RefreshAsync(RefreshRequest req, HttpContext http, CancellationToken ct) =>
        tokens.RefreshAsync(req.RefreshToken!, http, ct);

    // A6
    public async Task<LogoutResponse> LogoutAsync(long userId, CancellationToken ct)
    {
        await tokens.RevokeAllAsync(userId, ct);
        return new LogoutResponse(true);
    }

    private static AuthUser ToAuthUser(User u, bool isNewUser) => new(
        u.MemberId, u.FullName, u.Mobile, u.Email, u.Gender, u.ProfileFor,
        u.MobileVerified, u.EmailVerified, isNewUser, u.OnboardingStep);

    private async Task<string> NewMemberIdAsync(CancellationToken ct)
    {
        for (var i = 0; i < 20; i++)
        {
            var id = "DM-" + RandomNumberGenerator.GetInt32(100_000, 1_000_000);
            if (!await db.Users.AnyAsync(u => u.MemberId == id, ct)) return id;
        }
        throw new InvalidOperationException("Could not allocate a member id.");
    }
}
```
---

## Step 6: REPLACE the controllers

### 6a. `Controllers/AuthController.cs`
What changed: new `POST /v1/auth/otp/resend` endpoint.

```csharp
using KalyanaVaran.Api.Common;
using KalyanaVaran.Api.Contracts;
using KalyanaVaran.Api.Middleware;
using KalyanaVaran.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace KalyanaVaran.Api.Controllers;

[Route("v1/auth")]
[Tags("Auth")]
[EnableRateLimiting(RateLimitPolicies.AuthPolicy)]   // 10 req/min per IP
public sealed class AuthController(AuthService auth) : ApiControllerBase
{
    /// <summary>Creates a pending member and sends a mobile OTP (SMS) + email OTP through the configured providers.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EndpointSummary("A1 · Register + send OTP")]
    public async Task<ActionResult<ApiResponse<OtpSentResponse>>> Register(RegisterRequest req, CancellationToken ct) =>
        Envelope(await auth.RegisterAsync(req, HttpContext, ct));

    /// <summary>Existing member login, or OTP resend.</summary>
    [HttpPost("otp/send")]
    [AllowAnonymous]
    [EndpointSummary("A2 · Send / resend OTP (sms | whatsapp | email)")]
    public async Task<ActionResult<ApiResponse<OtpSentResponse>>> SendOtp(SendOtpRequest req, CancellationToken ct) =>
        Envelope(await auth.SendOtpAsync(req, HttpContext, ct));

    /// <summary>
    /// "Resend OTP" button. Same rules as otp/send: 30 s cooldown and 5 per hour per channel.
    /// Sending a new code makes the previous code of that channel invalid.
    /// </summary>
    [HttpPost("otp/resend")]
    [AllowAnonymous]
    [EndpointSummary("A2b · Resend OTP (sms | whatsapp | email)")]
    public async Task<ActionResult<ApiResponse<OtpSentResponse>>> ResendOtp(SendOtpRequest req, CancellationToken ct) =>
        Envelope(await auth.SendOtpAsync(req, HttpContext, ct));

    /// <summary>Returns access + refresh tokens. Copy tokens.accessToken into Scalar's Bearer auth.</summary>
    [HttpPost("otp/verify")]
    [AllowAnonymous]
    [EndpointSummary("A3 · Verify OTP → session")]
    public async Task<ActionResult<ApiResponse<AuthSession>>> VerifyOtp(VerifyOtpRequest req, CancellationToken ct) =>
        Envelope(await auth.VerifyOtpAsync(req, HttpContext, ct));

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EndpointSummary("A5 · Refresh tokens (rotates)")]
    public async Task<ActionResult<ApiResponse<AuthTokens>>> Refresh(RefreshRequest req, CancellationToken ct) =>
        Envelope(await auth.RefreshAsync(req, HttpContext, ct));

    [HttpPost("logout")]
    [Authorize]
    [EndpointSummary("A6 · Logout (revokes refresh tokens)")]
    public async Task<ActionResult<ApiResponse<LogoutResponse>>> Logout(CancellationToken ct) =>
        Envelope(await auth.LogoutAsync(CurrentUserId, ct));
}
```
### 6b. `Controllers/DevController.cs`
What changed: new `POST /v1/dev/test-delivery` endpoint for checking provider keys (Development only).

```csharp
using KalyanaVaran.Api.Common;
using KalyanaVaran.Api.Contracts;
using KalyanaVaran.Api.Data;
using KalyanaVaran.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace KalyanaVaran.Api.Controllers;

/// <summary>Testing helpers. Return 404 outside the Development environment.</summary>
[Route("v1/dev")]
[AllowAnonymous]
[Tags("Dev tools")]
public sealed class DevController(MediaService media, AppDbContext db, IWebHostEnvironment env) : ApiControllerBase
{
    /// <summary>Checks the Entity Framework connection to the database and counts rows per table.</summary>
    [HttpGet("db-check")]
    [EndpointSummary("DEV · Check database connection (EF Core)")]
    public async Task<ActionResult<ApiResponse<DbCheckResponse>>> DbCheck(CancellationToken ct)
    {
        if (!env.IsDevelopment()) throw ApiException.NotFound();

        var conn = db.Database.GetDbConnection();
        try
        {
            var counts = new Dictionary<string, int>
            {
                ["Users"] = await db.Users.CountAsync(ct),
                ["OtpRequests"] = await db.OtpRequests.CountAsync(ct),
                ["RefreshTokens"] = await db.RefreshTokens.CountAsync(ct),
                ["OnboardingSteps"] = await db.OnboardingSteps.CountAsync(ct),
                ["Profiles"] = await db.Profiles.CountAsync(ct),
                ["Siblings"] = await db.Siblings.CountAsync(ct),
                ["PartnerPreferences"] = await db.PartnerPreferences.CountAsync(ct),
                ["Photos"] = await db.Photos.CountAsync(ct),
                ["KycVerifications"] = await db.KycVerifications.CountAsync(ct)
            };
            var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();

            return Envelope(new DbCheckResponse(true, conn.DataSource, conn.Database, counts, applied, pending));
        }
        catch (Exception ex) when (ex is SqlException || ex.InnerException is SqlException)
        {
            var sql = ex as SqlException ?? (SqlException)ex.InnerException!;
            var hint = sql.Number switch
            {
                40615 => "Your IP is blocked by the Azure SQL firewall. Add it in Azure portal → SQL server → Networking.",
                18456 => "Login failed: check User ID / Password in the connection string (dotnet user-secrets).",
                4060 => "Database name not found: check Initial Catalog=KalyanaVaram.",
                208 => "A table is missing: run Database/KalyanaVaram_AzureSQL.sql.",
                207 => "A column is missing: run Database/KalyanaVaram_AzureSQL.sql (UPGRADE section).",
                _ => "See the SQL error message."
            };
            throw new ApiException(503, ErrorCodes.InternalError, $"Database error {sql.Number}: {hint} ({sql.Message})");
        }
    }

    /// <summary>
    /// Sends ONE test OTP (random code, not stored, not valid for login) through the configured
    /// SMS or email provider, to check your keys / DLT template / SMTP login.
    /// </summary>
    [HttpPost("test-delivery")]
    [EndpointSummary("DEV · Test SMS / email provider")]
    public async Task<ActionResult<ApiResponse<DevTestDeliveryResponse>>> TestDelivery(
        DevTestDeliveryRequest body,
        [FromServices] Services.Delivery.ISmsSender sms,
        [FromServices] Services.Delivery.IEmailSender email,
        [FromServices] Microsoft.Extensions.Options.IOptions<Services.Delivery.SmsOptions> smsOptions,
        [FromServices] Microsoft.Extensions.Options.IOptions<Services.Delivery.EmailOptions> emailOptions,
        CancellationToken ct)
    {
        if (!env.IsDevelopment()) throw ApiException.NotFound();

        var code = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var isEmail = body.Channel == "email";
        try
        {
            var id = isEmail
                ? await email.SendOtpAsync(body.To!, "Test User", code, 10, ct)
                : await sms.SendOtpAsync(body.To!, body.Channel, code, 10, ct);
            var provider = isEmail ? emailOptions.Value.Provider : smsOptions.Value.Provider;
            return Envelope(new DevTestDeliveryResponse(true, provider, id,
                provider.Equals("Console", StringComparison.OrdinalIgnoreCase)
                    ? "Console provider: nothing was really sent. Set a real provider to test delivery."
                    : $"Sent. Check the {(isEmail ? "inbox (and spam folder)" : "phone")}."));
        }
        catch (Services.Delivery.OtpDeliveryException ex)
        {
            throw new ApiException(ex.ProviderUnavailable ? 503 : 502, ErrorCodes.OtpDeliveryFailed,
                $"Provider error: {ex.Message} {ex.InnerException?.Message}".Trim());
        }
    }

    [HttpPost("kyc/{memberId}/review")]
    [EndpointSummary("DEV · Approve / reject a member's KYC")]
    public async Task<ActionResult<ApiResponse<VerificationResponse>>> ReviewKyc(
        string memberId, DevKycReviewRequest body, CancellationToken ct)
    {
        if (!env.IsDevelopment()) throw ApiException.NotFound();
        return Envelope(await media.ReviewKycAsync(memberId, body.Status!, body.Reason, ct));
    }
}
```
---

## Step 7: Small EDITS

### 7a. `Entities/Entities.cs`: class `OtpRequest`
- [ ] **Find:**
```csharp
    public DateTime? ConsumedAt { get; set; }
```
- [ ] **Add these 2 lines directly below it:**
```csharp
    public DateTime? VerifiedAt { get; set; }           // set only when this code was entered correctly
    public string? ProviderMessageId { get; set; }      // MSG91 / Twilio / SendGrid / SMTP message id (for support)
```
(If `Destination` is missing from `OtpRequest`, add `public string Destination { get; set; } = "";` too.)

### 7b. `Data/AppDbContext.cs`: inside `b.Entity<OtpRequest>(e => { ... })`
- [ ] **Find:**
```csharp
            e.Property(x => x.Destination).HasMaxLength(255);
```
- [ ] **Add below it:**
```csharp
            e.Property(x => x.ProviderMessageId).HasMaxLength(100);
```

### 7c. `Common/ApiResponse.cs`: class `ErrorCodes`
- [ ] **Find:**
```csharp
    public const string OtpRateLimited = "OTP_RATE_LIMITED";
```
- [ ] **Add below it:**
```csharp
    public const string OtpAlreadyUsed = "OTP_ALREADY_USED";
    public const string OtpDeliveryFailed = "OTP_DELIVERY_FAILED";
```

### 7d. `Contracts/ProfileContracts.cs`
- [ ] **Find:**
```csharp
/// <summary>Dev-only helper to approve/reject KYC while testing.</summary>
```
- [ ] **Paste this ABOVE it:**
```csharp
/// <summary>Dev-only: send a test message through the configured provider.</summary>
public sealed class DevTestDeliveryRequest
{
    /// <example>email</example>
    [AllowedValues("sms", "whatsapp", "email")] public string Channel { get; set; } = "sms";

    /// <summary>+91XXXXXXXXXX for sms/whatsapp, an email address for email.</summary>
    /// <example>you@example.com</example>
    [Required] public string? To { get; set; }
}

public sealed record DevTestDeliveryResponse(bool Sent, string Provider, string? ProviderMessageId, string Note);

```

### 7e. `Program.cs`: 3 edits

**Edit 1: usings at the top**
- [ ] **Find:** `using KalyanaVaran.Api.Services;`
- [ ] **Add below it:**
```csharp
using KalyanaVaran.Api.Services.Delivery;
```

**Edit 2: just after `var config = builder.Configuration;`**
- [ ] **Add:**
```csharp
// SMS_PROVIDER, SMS_API_KEY, EMAIL_HOST, EMAIL_PASSWORD … env vars (see docs/OTP_DELIVERY.md)
config.AddOtpDeliveryEnvironmentAliases();
```

**Edit 3: replace the fake senders**
- [ ] **Find these 2 lines** (in the "App services" section):
```csharp
builder.Services.AddSingleton<ISmsSender, ConsoleSmsSender>();
builder.Services.AddSingleton<IEmailSender, ConsoleEmailSender>();
```
- [ ] **Replace them with:**
```csharp
// Real OTP delivery: Sms:Provider = Fast2Sms | Msg91 | Twilio | Console(dev), Email:Provider = Smtp | SendGrid | Console(dev)
builder.Services.AddOtpDelivery(config, builder.Environment);
```

When you're done, the App services section looks like this:
```csharp
// ---------- App services ----------
// Real OTP delivery: Sms:Provider = Fast2Sms | Msg91 | Twilio | Console(dev), Email:Provider = Smtp | SendGrid | Console(dev)
builder.Services.AddOtpDelivery(config, builder.Environment);
builder.Services.AddScoped<OtpService>();
builder.Services.AddScoped<TokenService>();
...
```

---

## Step 8: NEW file `.env.example` (project root, next to `Program.cs`)
This is only an example list of setting names. Never put real keys in it.

```bash
# =====================================================================
# KalyanaVaran API - OTP delivery settings (example, NO real secrets here)
# .NET does not read .env files by itself. Use ONE of:
#   - Windows:  setx SMS_API_KEY "xxxx"     (then reopen the terminal)
#   - PowerShell (this window only):  $env:SMS_API_KEY="xxxx"
#   - Azure App Service: Configuration -> Application settings
#   - or dotnet user-secrets (see docs/OTP_DELIVERY.md)
# =====================================================================

# ---- SMS ----  Fast2Sms | Msg91 | Twilio | Console (dev only)
SMS_PROVIDER=Fast2Sms
SMS_API_KEY=                 # Fast2SMS API key / MSG91 AuthKey / Twilio Account SID
SMS_API_SECRET=              # Twilio Auth Token only
SMS_SENDER_ID=               # DLT sender id (6 letters, e.g. KVMTRY) / Twilio number
SMS_TEMPLATE_ID=             # Fast2SMS DLT message id / MSG91 template id
SMS_OTP_VARIABLE=otp         # MSG91 only: variable name in the template
SMS_EXPIRY_VARIABLE=         # set to "minutes" if your template also has the validity

# ---- Email ----  Smtp | SendGrid | Console (dev only)
EMAIL_PROVIDER=Smtp
EMAIL_HOST=smtp-relay.brevo.com   # Gmail: smtp.gmail.com  | Amazon SES: email-smtp.ap-south-1.amazonaws.com
EMAIL_PORT=587
EMAIL_USERNAME=              # Brevo SMTP login / Gmail address / SES SMTP user
EMAIL_PASSWORD=              # Brevo SMTP key / Gmail App Password / SES SMTP password
EMAIL_FROM=no-reply@yourdomain.com
EMAIL_FROM_NAME=KalyanaVaran Matrimony
EMAIL_API_KEY=               # SendGrid only

# ---- OTP ----
OTP_EXPIRY_SECONDS=600       # UI says "Valid for 10 mins"; 300 = 5 minutes
OTP_HASH_SECRET=             # long random string; changing it invalidates unused OTPs
```
---

## Step 9: Build, run and test

### 9a. Build
```powershell
cd D:\KVM-API-2\kalyanavaran-api
dotnet restore
dotnet build
```
✅ `Build succeeded`. If you see errors, check the table below or paste the error to Claude.

| Build error | Fix |
|---|---|
| `The type or namespace 'MailKit' could not be found` | Step 2 not saved; run `dotnet restore` again |
| `The namespace already contains a definition for 'ISmsSender'` (or `ConsoleSmsSender`) | The old classes are still at the top of `Services/OtpService.cs`. Redo step 5a |
| `'OtpRequest' does not contain a definition for 'VerifiedAt'` | Step 7a |
| `'ErrorCodes' does not contain 'OtpDeliveryFailed'` | Step 7c |
| `'DevTestDeliveryRequest' could not be found` | Step 7d |
| `'IServiceCollection' does not contain 'AddOtpDelivery'` | Step 4.9 file missing, or edit 1 of step 7e |

### 9b. Run in fake mode first (free, nothing is sent)
```powershell
dotnet run --launch-profile http
```
Open **http://localhost:5080/scalar**, then:
1. **A1 Register**, then **A3 Verify** with `"otp": "842910"`. ✅ Works as before.
2. **Dev tools → POST `/v1/dev/test-delivery`** with `{ "channel": "email", "to": "you@gmail.com" }`. ✅ `"provider": "Console"`, "nothing was really sent".

### 9c. Turn on real email (free, Gmail)
1. Google Account → Security → **2-Step Verification ON** → **App passwords** → create one → copy the 16 characters.
2. Stop the API, then run:
```powershell
dotnet user-secrets set "Email:Provider" "Smtp"
dotnet user-secrets set "Email:Host" "smtp.gmail.com"
dotnet user-secrets set "Email:Port" "587"
dotnet user-secrets set "Email:Username" "yourname@gmail.com"
dotnet user-secrets set "Email:Password" "abcd efgh ijkl mnop"
dotnet user-secrets set "Email:From" "yourname@gmail.com"
```
3. Start the API and test: **POST `/v1/dev/test-delivery`** `{ "channel": "email", "to": "yourname@gmail.com" }`. ✅ The email arrives (check spam too).
4. **A1 Register** with your real email. The email OTP is real; the SMS stays fake (842910).
5. **A3 Verify** with `"otp": "842910"` and `"emailOtp": "<code from the email>"`.

### 9d. Turn on real SMS (after DLT approval + Fast2SMS account)
```powershell
dotnet user-secrets set "Sms:Provider" "Fast2Sms"
dotnet user-secrets set "Sms:ApiKey" "<Fast2SMS API key>"
dotnet user-secrets set "Sms:SenderId" "KVMTRY"
dotnet user-secrets set "Sms:TemplateId" "<DLT message id>"
```
Test with **POST `/v1/dev/test-delivery`** `{ "channel": "sms", "to": "+91XXXXXXXXXX" }`, then the full Register → Verify flow.
Once the SMS provider is real, `842910` **stops working for SMS**. Use the code that arrives on the phone.

**To go back to fake mode:**
```powershell
dotnet user-secrets remove "Sms:Provider"
dotnet user-secrets remove "Email:Provider"
```

### 9e. Error cases to check

| Do this | Expected |
|---|---|
| Wrong OTP | 400 `OTP_INVALID` "… 4 attempts left" |
| Verify the same request again after success | 400 `OTP_ALREADY_USED` |
| **A2b Resend** within 30 s | 429 "Please wait N seconds" |
| Wrong `Email:Password` | 503 `OTP_DELIVERY_FAILED` (test-delivery shows "login failed") |
| Missing key at startup | API stops with a list of missing settings |

---

## Done: checklist
- [ ] Step 1 SQL run on KalyanaVaram
- [ ] Steps 2–3 csproj and settings replaced
- [ ] Step 4: 9 files in `Services/Delivery`
- [ ] Steps 5–6: 4 files replaced
- [ ] Step 7: 5 small edits
- [ ] Step 8: `.env.example`
- [ ] `dotnet build` succeeded
- [ ] Fake mode works (842910)
- [ ] Real email test received

Providers, costs and DLT registration are covered in `docs/OTP_DELIVERY.md`.
