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
