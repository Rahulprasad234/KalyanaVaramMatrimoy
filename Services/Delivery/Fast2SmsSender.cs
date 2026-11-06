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
