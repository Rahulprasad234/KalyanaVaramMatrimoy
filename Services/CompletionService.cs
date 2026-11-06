using KalyanaVaran.Api.Contracts;
using KalyanaVaran.Api.Data;
using KalyanaVaran.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace KalyanaVaran.Api.Services;

/// <summary>P5. percent = round(completedSteps / 6 × 100), same rule as the frontend mock.</summary>
public sealed class CompletionService(AppDbContext db)
{
    public const int TotalSteps = 6;

    public async Task<ProfileCompletion> GetAsync(long userId, CancellationToken ct)
    {
        var steps = await db.OnboardingSteps
            .Where(s => s.UserId == userId)
            .Select(s => (int)s.Step)
            .OrderBy(s => s)
            .ToListAsync(ct);

        var kycStatus = await db.KycVerifications
            .Where(k => k.UserId == userId)
            .OrderByDescending(k => k.SubmittedAt)
            .Select(k => k.Status)
            .FirstOrDefaultAsync(ct) ?? KycStatus.NotSubmitted;

        var pending = Enumerable.Range(1, TotalSteps).Where(s => !steps.Contains(s)).ToList();

        var missing = pending.Select(s => $"onboarding.step{s}").ToList();
        if (kycStatus is KycStatus.NotSubmitted or KycStatus.Rejected)
            missing.AddRange(["kyc.document", "kyc.selfie"]);

        var nextStep = pending.Count > 0 ? pending[0] : TotalSteps + 1;
        var percent = (int)Math.Round(steps.Count * 100.0 / TotalSteps, MidpointRounding.AwayFromZero);

        return new ProfileCompletion(percent, steps.ToArray(), nextStep, missing.ToArray(), kycStatus);
    }
}
