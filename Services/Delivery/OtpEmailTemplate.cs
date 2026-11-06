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
