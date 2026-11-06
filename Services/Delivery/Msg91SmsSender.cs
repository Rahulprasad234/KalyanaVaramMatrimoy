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
