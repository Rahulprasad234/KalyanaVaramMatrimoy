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
