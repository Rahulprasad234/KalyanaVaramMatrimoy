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
