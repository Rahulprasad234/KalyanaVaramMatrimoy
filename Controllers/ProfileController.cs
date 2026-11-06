using KalyanaVaran.Api.Common;
using KalyanaVaran.Api.Contracts;
using KalyanaVaran.Api.Middleware;
using KalyanaVaran.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace KalyanaVaran.Api.Controllers;

[Route("v1/profile/me")]
[Authorize]
public sealed class ProfileController(
    ProfileService profiles,
    OnboardingService onboarding,
    CompletionService completion,
    MediaService media) : ApiControllerBase
{
    // ---------- P1 / P2 ----------
    [HttpGet]
    [Tags("Profile")]
    [EndpointSummary("P1 · Get my profile")]
    public async Task<ActionResult<ApiResponse<MyProfileResponse>>> Get(CancellationToken ct) =>
        Envelope(await profiles.GetAsync(CurrentUserId, ct));

    [HttpPut]
    [Tags("Profile")]
    [EndpointSummary("P2 · Update my profile (partial)")]
    public async Task<ActionResult<ApiResponse<MyProfileResponse>>> Update(MyProfile body, CancellationToken ct) =>
        Envelope(await profiles.UpdateAsync(CurrentUserId, body, ct));

    // ---------- P3 ----------
    [HttpGet("onboarding/{step:int}")]
    [Tags("Onboarding")]
    [EndpointSummary("P3 · Get saved wizard step (1–6)")]
    public async Task<ActionResult<ApiResponse<GetStepResponse>>> GetStep(int step, CancellationToken ct) =>
        Envelope(await onboarding.GetAsync(CurrentUserId, step, ct));

    // ---------- P4 (one route per step so Scalar shows the right body) ----------
    [HttpPut("onboarding/1")]
    [Tags("Onboarding")]
    [EndpointSummary("P4 · Step 1 – Basic & physical")]
    public Task<ActionResult<ApiResponse<SaveStepResponse>>> PutStep1(Step1Request body, CancellationToken ct) => Save(1, body, ct);

    [HttpPut("onboarding/2")]
    [Tags("Onboarding")]
    [EndpointSummary("P4 · Step 2 – Religion & jathagam")]
    public Task<ActionResult<ApiResponse<SaveStepResponse>>> PutStep2(Step2Request body, CancellationToken ct) => Save(2, body, ct);

    [HttpPut("onboarding/3")]
    [Tags("Onboarding")]
    [EndpointSummary("P4 · Step 3 – Education & career")]
    public Task<ActionResult<ApiResponse<SaveStepResponse>>> PutStep3(Step3Request body, CancellationToken ct) => Save(3, body, ct);

    [HttpPut("onboarding/4")]
    [Tags("Onboarding")]
    [EndpointSummary("P4 · Step 4 – Family & lifestyle")]
    public Task<ActionResult<ApiResponse<SaveStepResponse>>> PutStep4(Step4Request body, CancellationToken ct) => Save(4, body, ct);

    [HttpPut("onboarding/5")]
    [Tags("Onboarding")]
    [EndpointSummary("P4 · Step 5 – Photos & ID type")]
    public Task<ActionResult<ApiResponse<SaveStepResponse>>> PutStep5(Step5Request body, CancellationToken ct) => Save(5, body, ct);

    [HttpPut("onboarding/6")]
    [Tags("Onboarding")]
    [EndpointSummary("P4 · Step 6 – Partner preferences")]
    public Task<ActionResult<ApiResponse<SaveStepResponse>>> PutStep6(Step6Request body, CancellationToken ct) => Save(6, body, ct);

    /// <summary>Any other step number → 404 NOT_FOUND.</summary>
    [HttpPut("onboarding/{step:int}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult PutUnknownStep(int step) => throw ApiException.NotFound("Onboarding step must be 1 to 6.");

    // ---------- P5 ----------
    [HttpGet("completion")]
    [Tags("Profile")]
    [EndpointSummary("P5 · Profile completion")]
    public async Task<ActionResult<ApiResponse<ProfileCompletion>>> Completion(CancellationToken ct) =>
        Envelope(await completion.GetAsync(CurrentUserId, ct));

    // ---------- P6 / P7 ----------
    [HttpPost("photos")]
    [Consumes("multipart/form-data")]
    [Tags("Photos & KYC")]
    [EnableRateLimiting(RateLimitPolicies.UploadsPolicy)]
    [EndpointSummary("P6 · Upload photo")]
    public async Task<ActionResult<ApiResponse<PhotoUploadResponse>>> UploadPhoto(IFormFile file, CancellationToken ct) =>
        Envelope(await media.UploadPhotoAsync(CurrentUserId, file, BaseUrl, ct));

    [HttpDelete("photos")]
    [Tags("Photos & KYC")]
    [EndpointSummary("P7 · Remove photo")]
    public async Task<ActionResult<ApiResponse<PhotosResponse>>> DeletePhoto(DeletePhotoRequest body, CancellationToken ct) =>
        Envelope(await media.DeletePhotoAsync(CurrentUserId, body.Url!, ct));

    // ---------- P8 / P9 ----------
    [HttpPost("verification")]
    [Consumes("multipart/form-data")]
    [Tags("Photos & KYC")]
    [EnableRateLimiting(RateLimitPolicies.UploadsPolicy)]
    [EndpointSummary("P8 · Submit KYC (ID + selfie)")]
    public async Task<ActionResult<ApiResponse<VerificationResponse>>> SubmitKyc([FromForm] KycSubmitForm form, CancellationToken ct) =>
        Envelope(await media.SubmitKycAsync(CurrentUserId, form, ct));

    [HttpGet("verification")]
    [Tags("Photos & KYC")]
    [EndpointSummary("P9 · KYC status")]
    public async Task<ActionResult<ApiResponse<VerificationResponse>>> GetKyc(CancellationToken ct) =>
        Envelope(await media.GetKycAsync(CurrentUserId, ct));

    private async Task<ActionResult<ApiResponse<SaveStepResponse>>> Save(int step, object body, CancellationToken ct) =>
        Envelope(await onboarding.SaveAsync(CurrentUserId, step, body, ct));
}
