using System.Text.Json;
using KalyanaVaran.Api.Common;
using KalyanaVaran.Api.Contracts;
using KalyanaVaran.Api.Data;
using KalyanaVaran.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace KalyanaVaran.Api.Services;

/// <summary>
/// P3 / P4. Every step is stored twice: raw JSON in onboarding_steps (for restore) and
/// mapped into profiles / siblings / partner_preferences (for /profile/me and matching).
/// </summary>
public sealed class OnboardingService(AppDbContext db, CompletionService completion)
{
    public static readonly JsonSerializerOptions RawJson = new(JsonSerializerDefaults.Web);

    // P3
    public async Task<GetStepResponse> GetAsync(long userId, int step, CancellationToken ct)
    {
        EnsureStep(step);
        var record = await db.OnboardingSteps.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Step == step, ct);

        JsonElement? data = null;
        if (record is not null)
        {
            using var doc = JsonDocument.Parse(record.Data);
            data = doc.RootElement.Clone();
        }
        return new GetStepResponse(step, data);
    }

    // P4
    public Task<SaveStepResponse> SaveAsync(long userId, int step, object payload, CancellationToken ct)
    {
        EnsureStep(step);
        // With EnableRetryOnFailure, a manual transaction must run inside the execution strategy
        // so the whole unit (not single statements) is retried after a transient Azure SQL error.
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(() =>
        {
            db.ChangeTracker.Clear();   // clean start on each retry attempt
            return SaveOnceAsync(userId, step, payload, ct);
        });
    }

    private async Task<SaveStepResponse> SaveOnceAsync(long userId, int step, object payload, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var user = await db.Users
            .Include(u => u.Profile)
            .Include(u => u.PartnerPreference)
            .Include(u => u.Siblings)
            .Include(u => u.Photos.Where(p => p.DeletedAt == null))
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw ApiException.Unauthorized();

        var profile = user.Profile;
        if (profile is null)
        {
            var (first, last) = ProfileParsing.SplitName(user.FullName);
            profile = new Profile { UserId = user.Id, FirstName = first, LastName = last };
            db.Profiles.Add(profile);
        }

        switch (payload)
        {
            case Step1Request s1: MapStep1(profile, s1); break;
            case Step2Request s2: MapStep2(profile, s2); break;
            case Step3Request s3: MapStep3(profile, s3); break;
            case Step4Request s4: MapStep4(user, profile, s4); break;
            case Step5Request s5: MapStep5(user, profile, s5); break;
            case Step6Request s6: MapStep6(user, s6, now); break;
            default: throw ApiException.NotFound("Unknown onboarding step.");
        }
        profile.UpdatedAt = now;

        var json = JsonSerializer.Serialize(payload, payload.GetType(), RawJson);
        var record = await db.OnboardingSteps.FirstOrDefaultAsync(s => s.UserId == userId && s.Step == step, ct);
        if (record is null)
            db.OnboardingSteps.Add(new OnboardingStepRecord { UserId = userId, Step = (short)step, Data = json, CompletedAt = now });
        else
        {
            record.Data = json;
            record.CompletedAt = now;
        }
        await db.SaveChangesAsync(ct);

        var result = await completion.GetAsync(userId, ct);
        user.OnboardingStep = (short)result.NextStep;
        user.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
        return new SaveStepResponse(step, true, result);
    }

    private static void EnsureStep(int step)
    {
        if (step is < 1 or > CompletionService.TotalSteps) throw ApiException.NotFound("Onboarding step must be 1 to 6.");
    }

    private static void MapStep1(Profile p, Step1Request s)
    {
        ProfileParsing.TryBuildDob(s.DobDay, s.DobMonth, s.DobYear, out var dob);
        ProfileParsing.TryParseHeightCm(s.Height, out var cm);
        p.DateOfBirth = dob;
        p.MaritalStatus = s.Marital;
        p.HeightCm = cm;
        p.WeightKg = short.TryParse(s.WeightKg, out var w) ? w : null;
        p.BodyType = s.BodyType;
        p.Complexion = s.Complexion;
        p.HasDisability = s.Disability == "Yes";
    }

    private static void MapStep2(Profile p, Step2Request s)
    {
        p.Religion = s.Religion;
        p.OtherReligion = s.Religion == "Other" ? s.OtherReligion : null;
        p.Caste = s.Caste;
        p.OtherCaste = s.Caste == "Other Community" ? s.OtherCaste : null;
        p.SubCaste = s.SubCaste;
        p.Gothram = s.Gothram;
        p.Nakshatra = s.Nakshatra;
        p.Rasi = s.Rasi;
        p.Dosham = s.Dosham;
        p.BirthTime = ProfileParsing.TryParseBirthTime(s.BirthTime, out var t) ? t : null;
        p.BirthPlace = s.BirthPlace;
        p.AutoGenerateChart = s.AutoGenerateChart;
        p.HoroscopeUrls = s.HoroscopeFileUrls ?? [];
    }

    private static void MapStep3(Profile p, Step3Request s)
    {
        p.Education = s.Education;
        p.OtherEducation = s.Education == "Other" ? s.OtherEducation : null;
        p.College = s.College;
        p.EmploymentSector = s.EmploymentSector;
        p.Profession = s.Profession;
        p.Employer = s.Employer;
        p.AnnualIncome = s.AnnualIncome;
        p.IncomeBand = s.IncomeBand;
        p.WorkLocation = s.WorkLocation;
        p.OpenToRelocate = s.OpenToRelocate;
    }

    private void MapStep4(User user, Profile p, Step4Request s)
    {
        p.FamilyType = s.FamilyType;
        p.FamilyValues = s.FamilyValues;
        p.FatherOccupation = s.FatherOccupation;
        p.MotherOccupation = s.MotherOccupation;
        p.Diet = s.Diet;
        p.Smoking = s.Smoking;
        p.Drinking = s.Drinking;
        p.About = s.About;

        var custom = s.CustomHobbies ?? [];
        p.CustomHobbies = custom.Count == 0 ? null : JsonSerializer.Serialize(custom, RawJson);
        p.Hobbies = (s.Hobbies ?? [])
            .Concat(custom.Select(c => c.Name!))
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        ReplaceSiblings(db, user, s.BrotherList, s.SisterList);
    }

    private static void MapStep5(User user, Profile p, Step5Request s)
    {
        p.PhotoBlur = s.Blur;
        p.IdType = s.IdType;
        if (s.Photos is not null) ReorderPhotos(user, s.Photos);
    }

    private void MapStep6(User user, Step6Request s, DateTime now)
    {
        var pref = user.PartnerPreference;
        if (pref is null)
        {
            pref = new PartnerPreference { UserId = user.Id };
            db.PartnerPreferences.Add(pref);
        }

        var custom = s.CustomCities ?? [];
        pref.AgeMin = s.PartnerAgeMin;
        pref.AgeMax = s.PartnerAgeMax;
        pref.HeightMinCm = s.PartnerHeightMin;
        pref.HeightMaxCm = s.PartnerHeightMax;
        pref.Marital = s.PartnerMarital ?? [];
        pref.Languages = s.PartnerLanguages ?? [];
        pref.AnyMotherTongue = s.AnyMotherTongue;
        pref.InterCaste = s.InterCaste;
        pref.Religion = s.PartnerReligion;
        pref.Caste = s.PartnerCaste;
        pref.OtherCaste = s.OtherPartnerCaste;
        pref.SubCaste = s.PartnerSubCaste;
        pref.OtherSubCaste = s.OtherPartnerSubCaste;
        pref.JathagamMatch = s.JathagamMatch;
        pref.Dosham = s.PartnerDosham;
        pref.Education = s.PartnerEducation ?? [];
        pref.Employment = s.PartnerEmployment ?? [];
        pref.Income = s.PartnerIncome;
        pref.CustomCities = custom.Count == 0 ? null : JsonSerializer.Serialize(custom, RawJson);
        pref.Locations = (s.PartnerLocations ?? [])
            .Concat(custom.Select(c => c.Name!))
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        pref.OpenToRelocate = s.PartnerOpenToRelocate;
        pref.UpdatedAt = now;
    }

    /// <summary>Shared with PUT /profile/me. A null list leaves that relation unchanged.</summary>
    internal static void ReplaceSiblings(AppDbContext db, User user, List<SiblingEntry>? brothers, List<SiblingEntry>? sisters)
    {
        void Replace(string relation, List<SiblingEntry>? list)
        {
            if (list is null) return;
            var old = user.Siblings.Where(x => x.Relation == relation).ToList();
            db.Siblings.RemoveRange(old);
            foreach (var o in old) user.Siblings.Remove(o);

            short order = 0;
            foreach (var e in list)
            {
                var sib = new Sibling
                {
                    UserId = user.Id, Relation = relation, ClientId = e.Id, Name = e.Name!.Trim(),
                    Age = e.Age, MaritalStatus = e.MaritalStatus, SortOrder = order++
                };
                db.Siblings.Add(sib);
                user.Siblings.Add(sib);
            }
        }

        Replace("brother", brothers);
        Replace("sister", sisters);
    }

    /// <summary>Order of the URL list = display order; first one is the primary photo.</summary>
    private static void ReorderPhotos(User user, List<string> urls)
    {
        var mine = user.Photos.ToDictionary(p => p.Url);
        var unknown = urls.FirstOrDefault(u => !mine.ContainsKey(u));
        if (unknown is not null)
            throw ApiException.Validation("One of the photos does not belong to your profile. Please upload it again.", "photos");

        var ordered = urls.Distinct().Select(u => mine[u])
            .Concat(user.Photos.Where(p => !urls.Contains(p.Url)).OrderBy(p => p.SortOrder))
            .ToList();
        for (short i = 0; i < ordered.Count; i++)
        {
            ordered[i].SortOrder = i;
            ordered[i].IsPrimary = i == 0;
        }
    }
}
