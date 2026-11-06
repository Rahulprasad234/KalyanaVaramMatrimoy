using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using KalyanaVaran.Api.Common;

namespace KalyanaVaran.Api.Contracts;

public sealed class SiblingEntry
{
    [StringLength(50)] public string? Id { get; set; }
    [Required, StringLength(100)] public string? Name { get; set; }
    [StringLength(10)] public string? Age { get; set; }
    [Required, AllowedValues("Married", "Unmarried")] public string? MaritalStatus { get; set; }
}

public sealed class NameIcon
{
    [Required, StringLength(60)] public string? Name { get; set; }
    [StringLength(60)] public string? Icon { get; set; }
}

// ---------- Step 1: Basic & Physical ----------
public sealed class Step1Request : IValidatableObject
{
    /// <example>14</example>
    [Required] public string? DobDay { get; set; }
    /// <example>August</example>
    [Required] public string? DobMonth { get; set; }
    /// <example>1997</example>
    [Required] public string? DobYear { get; set; }

    /// <example>Never Married</example>
    [Required, AllowedValues("Never Married", "Divorced", "Widowed", "Awaiting Divorce")]
    public string? Marital { get; set; }

    /// <example>5'5" (165 cm)</example>
    [Required] public string? Height { get; set; }
    /// <example>62</example>
    public string? WeightKg { get; set; }
    /// <example>Athletic</example>
    public string? BodyType { get; set; }
    /// <example>Wheatish</example>
    public string? Complexion { get; set; }

    /// <example>No</example>
    [Required, AllowedValues("No", "Yes")] public string? Disability { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!ProfileParsing.TryBuildDob(DobDay, DobMonth, DobYear, out var dob))
            yield return new ValidationResult("Please enter a valid date of birth.", new[] { nameof(DobDay) });
        else if (ProfileParsing.AgeOn(dob, DateOnly.FromDateTime(DateTime.UtcNow)) < 18)
            yield return new ValidationResult("Minimum age is 18 years.", new[] { nameof(DobYear) });

        if (!ProfileParsing.TryParseHeightCm(Height, out _))
            yield return new ValidationResult("Please select a valid height.", new[] { nameof(Height) });

        if (!string.IsNullOrWhiteSpace(WeightKg) && !(short.TryParse(WeightKg, out var w) && w is >= 30 and <= 250))
            yield return new ValidationResult("Weight must be between 30 and 250 kg.", new[] { nameof(WeightKg) });
    }
}

// ---------- Step 2: Religion & Jathagam ----------
public sealed class Step2Request : IValidatableObject
{
    /// <example>Hindu</example>
    [Required, StringLength(60)] public string? Religion { get; set; }
    [StringLength(60)] public string? OtherReligion { get; set; }
    /// <example>Brahmin - Iyer</example>
    [StringLength(80)] public string? Caste { get; set; }
    [StringLength(80)] public string? OtherCaste { get; set; }
    [StringLength(80)] public string? SubCaste { get; set; }
    [StringLength(80)] public string? Gothram { get; set; }
    [StringLength(80)] public string? Nakshatra { get; set; }
    [StringLength(80)] public string? Rasi { get; set; }
    /// <example>No Dosham</example>
    [StringLength(80)] public string? Dosham { get; set; }
    /// <example>06:45 AM</example>
    public string? BirthTime { get; set; }
    [StringLength(120)] public string? BirthPlace { get; set; }
    public bool AutoGenerateChart { get; set; }
    public List<string>? HoroscopeFileUrls { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Religion == "Other" && string.IsNullOrWhiteSpace(OtherReligion))
            yield return new ValidationResult("Please specify your religion.", new[] { nameof(OtherReligion) });
        if (Caste == "Other Community" && string.IsNullOrWhiteSpace(OtherCaste))
            yield return new ValidationResult("Please specify your community.", new[] { nameof(OtherCaste) });
        if (!string.IsNullOrWhiteSpace(BirthTime) && !ProfileParsing.TryParseBirthTime(BirthTime, out _))
            yield return new ValidationResult("Birth time must look like 06:45 AM.", new[] { nameof(BirthTime) });
    }
}

// ---------- Step 3: Education & Career ----------
public sealed class Step3Request : IValidatableObject
{
    /// <example>B.Tech / B.E. (Computer Science / IT)</example>
    [Required, StringLength(120)] public string? Education { get; set; }
    [StringLength(120)] public string? OtherEducation { get; set; }
    [StringLength(150)] public string? College { get; set; }
    /// <example>Private Sector</example>
    [Required, StringLength(60)] public string? EmploymentSector { get; set; }
    [StringLength(100)] public string? Profession { get; set; }
    [StringLength(150)] public string? Employer { get; set; }
    /// <example>₹18 Lakhs - ₹25 Lakhs per annum</example>
    [StringLength(80)] public string? AnnualIncome { get; set; }
    /// <example>₹18 - 25L</example>
    [StringLength(30)] public string? IncomeBand { get; set; }
    /// <example>Chennai</example>
    [StringLength(100)] public string? WorkLocation { get; set; }
    public bool OpenToRelocate { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Education == "Other" && string.IsNullOrWhiteSpace(OtherEducation))
            yield return new ValidationResult("Please specify your education.", new[] { nameof(OtherEducation) });
    }
}

// ---------- Step 4: Family & Lifestyle ----------
public sealed class Step4Request
{
    /// <example>Nuclear Family</example>
    [StringLength(60)] public string? FamilyType { get; set; }
    /// <example>Moderate</example>
    [StringLength(60)] public string? FamilyValues { get; set; }
    [StringLength(100)] public string? FatherOccupation { get; set; }
    [StringLength(100)] public string? MotherOccupation { get; set; }
    [MaxLength(10)] public List<SiblingEntry>? BrotherList { get; set; }
    [MaxLength(10)] public List<SiblingEntry>? SisterList { get; set; }
    /// <example>Vegetarian</example>
    [StringLength(30)] public string? Diet { get; set; }
    /// <example>Never</example>
    [StringLength(30)] public string? Smoking { get; set; }
    /// <example>Never</example>
    [StringLength(30)] public string? Drinking { get; set; }
    [MaxLength(30)] public List<string>? Hobbies { get; set; }
    [MaxLength(20)] public List<NameIcon>? CustomHobbies { get; set; }
    [StringLength(2000)] public string? About { get; set; }
}

// ---------- Step 5: Photos & KYC choice ----------
public sealed class Step5Request
{
    /// <summary>URLs returned by POST /profile/me/photos, in display order (first = primary).</summary>
    public List<string>? Photos { get; set; }
    public bool Blur { get; set; }
    /// <example>Aadhaar Card</example>
    [AllowedValues(null, "Aadhaar Card", "Passport", "PAN Card", "Driving License")]
    public string? IdType { get; set; }
}

// ---------- Step 6: Partner preferences ----------
public sealed class Step6Request : IValidatableObject
{
    /// <example>25</example>
    [Range(18, 80)] public int? PartnerAgeMin { get; set; }
    /// <example>32</example>
    [Range(18, 80)] public int? PartnerAgeMax { get; set; }
    /// <example>165</example>
    [Range(120, 230)] public int? PartnerHeightMin { get; set; }
    /// <example>185</example>
    [Range(120, 230)] public int? PartnerHeightMax { get; set; }
    public List<string>? PartnerMarital { get; set; }
    public List<string>? PartnerLanguages { get; set; }
    public bool AnyMotherTongue { get; set; }
    public bool InterCaste { get; set; }
    [StringLength(60)] public string? PartnerReligion { get; set; }
    [StringLength(80)] public string? PartnerCaste { get; set; }
    [StringLength(80)] public string? OtherPartnerCaste { get; set; }
    [StringLength(80)] public string? PartnerSubCaste { get; set; }
    [StringLength(80)] public string? OtherPartnerSubCaste { get; set; }
    public bool JathagamMatch { get; set; }
    [StringLength(60)] public string? PartnerDosham { get; set; }
    public List<string>? PartnerEducation { get; set; }
    public List<string>? PartnerEmployment { get; set; }
    [StringLength(40)] public string? PartnerIncome { get; set; }
    [MaxLength(30)] public List<string>? PartnerLocations { get; set; }
    [MaxLength(20)] public List<NameIcon>? CustomCities { get; set; }
    public bool PartnerOpenToRelocate { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PartnerAgeMin > PartnerAgeMax)
            yield return new ValidationResult("Minimum age cannot be more than maximum age.", new[] { nameof(PartnerAgeMin) });
        if (PartnerHeightMin > PartnerHeightMax)
            yield return new ValidationResult("Minimum height cannot be more than maximum height.", new[] { nameof(PartnerHeightMin) });
    }
}

// ---------- Responses ----------
public sealed record ProfileCompletion(
    int Percent, int[] CompletedSteps, int NextStep, string[] MissingFields, string VerificationStatus);

public sealed record SaveStepResponse(int Step, bool Saved, ProfileCompletion Completion);

public sealed record GetStepResponse(int Step, JsonElement? Data);
