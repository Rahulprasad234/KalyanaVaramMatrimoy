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
