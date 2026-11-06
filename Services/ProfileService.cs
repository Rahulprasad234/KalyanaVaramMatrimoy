using KalyanaVaran.Api.Common;
using KalyanaVaran.Api.Contracts;
using KalyanaVaran.Api.Data;
using KalyanaVaran.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace KalyanaVaran.Api.Services;

/// <summary>P1 / P2: the MyProfile document assembled from profiles + siblings + photos + partner_preferences.</summary>
public sealed class ProfileService(AppDbContext db, CompletionService completion)
{
    public async Task<MyProfileResponse> GetAsync(long userId, CancellationToken ct)
    {
        var user = await LoadAsync(userId, tracking: false, ct);
        return await ToResponseAsync(user, ct);
    }

    public async Task<MyProfileResponse> UpdateAsync(long userId, MyProfile u, CancellationToken ct)
    {
        if (u.PartnerAgeMin > u.PartnerAgeMax)
            throw ApiException.Validation("Minimum age cannot be more than maximum age.", "partnerAgeMin");
        if (u.PartnerHeightMin > u.PartnerHeightMax)
            throw ApiException.Validation("Minimum height cannot be more than maximum height.", "partnerHeightMin");

        TimeOnly? birthTime = null;
        if (u.BirthTime is not null)
        {
            if (!ProfileParsing.TryParseBirthTime(u.BirthTime, out var t))
                throw ApiException.Validation("Birth time must look like 06:45.", "birthTime");
            birthTime = t;
        }

        var now = DateTime.UtcNow;
        var user = await LoadAsync(userId, tracking: true, ct);

        var p = user.Profile;
        if (p is null)
        {
            p = new Profile { UserId = user.Id };
            db.Profiles.Add(p);
            user.Profile = p;
        }

        if (u.FirstName is not null) p.FirstName = u.FirstName.Trim();
        if (u.LastName is not null) p.LastName = u.LastName.Trim();
        if (u.Blur is not null) p.PhotoBlur = u.Blur.Value;
        if (u.Religion is not null) p.Religion = u.Religion;
        if (u.OtherReligion is not null) p.OtherReligion = u.OtherReligion;
        if (u.Caste is not null) p.Caste = u.Caste;
        if (u.OtherCaste is not null) p.OtherCaste = u.OtherCaste;
        if (u.SubCaste is not null) p.SubCaste = u.SubCaste;
        if (u.Gothram is not null) p.Gothram = u.Gothram;
        if (u.Nakshatra is not null) p.Nakshatra = u.Nakshatra;
        if (u.Rasi is not null) p.Rasi = u.Rasi;
        if (birthTime is not null) p.BirthTime = birthTime;
        if (u.BirthPlace is not null) p.BirthPlace = u.BirthPlace;
        if (u.Dosham is not null) p.Dosham = u.Dosham;
        if (u.Education is not null) p.Education = u.Education;
        if (u.OtherEducation is not null) p.OtherEducation = u.OtherEducation;
        if (u.College is not null) p.College = u.College;
        if (u.EmploymentSector is not null) p.EmploymentSector = u.EmploymentSector;
        if (u.Profession is not null) p.Profession = u.Profession;
        if (u.Employer is not null) p.Employer = u.Employer;
        if (u.AnnualIncome is not null) p.AnnualIncome = u.AnnualIncome;
        if (u.WorkLocation is not null) p.WorkLocation = u.WorkLocation;
        if (u.OpenToRelocate is not null) p.OpenToRelocate = u.OpenToRelocate;
        if (u.FamilyType is not null) p.FamilyType = u.FamilyType;
        if (u.FamilyValues is not null) p.FamilyValues = u.FamilyValues;
        if (u.FatherOccupation is not null) p.FatherOccupation = u.FatherOccupation;
        if (u.MotherOccupation is not null) p.MotherOccupation = u.MotherOccupation;
        if (u.Diet is not null) p.Diet = u.Diet;
        if (u.Smoking is not null) p.Smoking = u.Smoking;
        if (u.Drinking is not null) p.Drinking = u.Drinking;
        if (u.Hobbies is not null) p.Hobbies = u.Hobbies;
        if (u.Interests is not null) p.Interests = u.Interests;
        if (u.About is not null) p.About = u.About;
        p.UpdatedAt = now;

        OnboardingService.ReplaceSiblings(db, user, u.BrotherList, u.SisterList);

        var touchesPartner = u.PartnerAgeMin is not null || u.PartnerAgeMax is not null ||
                             u.PartnerHeightMin is not null || u.PartnerHeightMax is not null ||
                             u.PartnerMarital is not null || u.PartnerEducation is not null ||
                             u.PartnerEmployment is not null || u.PartnerIncome is not null ||
                             u.PartnerLocations is not null || u.PartnerExpectations is not null;
        if (touchesPartner)
        {
            var pref = user.PartnerPreference;
            if (pref is null)
            {
                pref = new PartnerPreference { UserId = user.Id };
                db.PartnerPreferences.Add(pref);
                user.PartnerPreference = pref;
            }
            if (u.PartnerAgeMin is not null) pref.AgeMin = u.PartnerAgeMin;
            if (u.PartnerAgeMax is not null) pref.AgeMax = u.PartnerAgeMax;
            if (u.PartnerHeightMin is not null) pref.HeightMinCm = u.PartnerHeightMin;
            if (u.PartnerHeightMax is not null) pref.HeightMaxCm = u.PartnerHeightMax;
            if (u.PartnerMarital is not null) pref.Marital = u.PartnerMarital;
            if (u.PartnerEducation is not null) pref.Education = u.PartnerEducation;
            if (u.PartnerEmployment is not null) pref.Employment = u.PartnerEmployment;
            if (u.PartnerIncome is not null) pref.Income = u.PartnerIncome;
            if (u.PartnerLocations is not null) pref.Locations = u.PartnerLocations;
            if (u.PartnerExpectations is not null) pref.Expectations = u.PartnerExpectations;
            pref.UpdatedAt = now;
        }

        user.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return await ToResponseAsync(user, ct);
    }

    private async Task<User> LoadAsync(long userId, bool tracking, CancellationToken ct)
    {
        IQueryable<User> q = db.Users
            .Include(u => u.Profile)
            .Include(u => u.PartnerPreference)
            .Include(u => u.Siblings)
            .Include(u => u.Photos.Where(p => p.DeletedAt == null));
        if (!tracking) q = q.AsNoTracking();
        return await q.AsSplitQuery().FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw ApiException.Unauthorized();
    }

    private async Task<MyProfileResponse> ToResponseAsync(User user, CancellationToken ct)
    {
        var p = user.Profile ?? new Profile();
        var pref = user.PartnerPreference ?? new PartnerPreference();

        List<SiblingEntry> Sibs(string relation) => user.Siblings
            .Where(s => s.Relation == relation)
            .OrderBy(s => s.SortOrder)
            .Select(s => new SiblingEntry { Id = s.ClientId ?? s.Id.ToString(), Name = s.Name, Age = s.Age, MaritalStatus = s.MaritalStatus })
            .ToList();

        var dto = new MyProfile
        {
            FirstName = p.FirstName,
            LastName = p.LastName,
            Photos = user.Photos.Where(x => x.DeletedAt == null).OrderBy(x => x.SortOrder).Select(x => x.Url).ToList(),
            Blur = p.PhotoBlur,
            Religion = p.Religion,
            OtherReligion = p.OtherReligion,
            Caste = p.Caste,
            OtherCaste = p.OtherCaste,
            SubCaste = p.SubCaste,
            Gothram = p.Gothram,
            Nakshatra = p.Nakshatra,
            Rasi = p.Rasi,
            BirthTime = ProfileParsing.FormatBirthTime(p.BirthTime),
            BirthPlace = p.BirthPlace,
            Dosham = p.Dosham,
            Education = p.Education,
            OtherEducation = p.OtherEducation,
            College = p.College,
            EmploymentSector = p.EmploymentSector,
            Profession = p.Profession,
            Employer = p.Employer,
            AnnualIncome = p.AnnualIncome,
            WorkLocation = p.WorkLocation,
            OpenToRelocate = p.OpenToRelocate,
            FamilyType = p.FamilyType,
            FamilyValues = p.FamilyValues,
            FatherOccupation = p.FatherOccupation,
            MotherOccupation = p.MotherOccupation,
            BrotherList = Sibs("brother"),
            SisterList = Sibs("sister"),
            Diet = p.Diet,
            Smoking = p.Smoking,
            Drinking = p.Drinking,
            Hobbies = p.Hobbies,
            Interests = p.Interests,
            About = p.About,
            PartnerAgeMin = pref.AgeMin,
            PartnerAgeMax = pref.AgeMax,
            PartnerHeightMin = pref.HeightMinCm,
            PartnerHeightMax = pref.HeightMaxCm,
            PartnerMarital = pref.Marital,
            PartnerEducation = pref.Education,
            PartnerEmployment = pref.Employment,
            PartnerIncome = pref.Income,
            PartnerLocations = pref.Locations,
            PartnerExpectations = pref.Expectations
        };

        var updatedAt = new[] { user.UpdatedAt, p.UpdatedAt, pref.UpdatedAt }.Max();
        var comp = await completion.GetAsync(user.Id, ct);
        return new MyProfileResponse(user.MemberId, dto, comp, updatedAt);
    }
}
