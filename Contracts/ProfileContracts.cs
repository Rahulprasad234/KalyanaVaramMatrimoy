using System.ComponentModel.DataAnnotations;

namespace KalyanaVaran.Api.Contracts;

/// <summary>
/// MyProfile document (§6.1). Used for GET /profile/me and as a partial-update body for PUT /profile/me
/// (null = leave unchanged). `photos` is read-only here: use the photo endpoints.
/// </summary>
public sealed class MyProfile
{
    [StringLength(60)] public string? FirstName { get; set; }
    [StringLength(60)] public string? LastName { get; set; }
    public List<string>? Photos { get; set; }
    public bool? Blur { get; set; }

    [StringLength(60)] public string? Religion { get; set; }
    [StringLength(60)] public string? OtherReligion { get; set; }
    [StringLength(80)] public string? Caste { get; set; }
    [StringLength(80)] public string? OtherCaste { get; set; }
    [StringLength(80)] public string? SubCaste { get; set; }
    [StringLength(80)] public string? Gothram { get; set; }
    [StringLength(80)] public string? Nakshatra { get; set; }
    [StringLength(80)] public string? Rasi { get; set; }
    /// <example>06:45</example>
    public string? BirthTime { get; set; }
    [StringLength(120)] public string? BirthPlace { get; set; }
    [StringLength(80)] public string? Dosham { get; set; }

    [StringLength(120)] public string? Education { get; set; }
    [StringLength(120)] public string? OtherEducation { get; set; }
    [StringLength(150)] public string? College { get; set; }
    [StringLength(60)] public string? EmploymentSector { get; set; }
    [StringLength(100)] public string? Profession { get; set; }
    [StringLength(150)] public string? Employer { get; set; }
    [StringLength(80)] public string? AnnualIncome { get; set; }
    [StringLength(100)] public string? WorkLocation { get; set; }
    public bool? OpenToRelocate { get; set; }

    [StringLength(60)] public string? FamilyType { get; set; }
    [StringLength(60)] public string? FamilyValues { get; set; }
    [StringLength(100)] public string? FatherOccupation { get; set; }
    [StringLength(100)] public string? MotherOccupation { get; set; }
    [MaxLength(10)] public List<SiblingEntry>? BrotherList { get; set; }
    [MaxLength(10)] public List<SiblingEntry>? SisterList { get; set; }
    [StringLength(30)] public string? Diet { get; set; }
    [StringLength(30)] public string? Smoking { get; set; }
    [StringLength(30)] public string? Drinking { get; set; }
    [MaxLength(30)] public List<string>? Hobbies { get; set; }
    [MaxLength(30)] public List<string>? Interests { get; set; }
    [StringLength(2000)] public string? About { get; set; }

    [Range(18, 80)] public int? PartnerAgeMin { get; set; }
    [Range(18, 80)] public int? PartnerAgeMax { get; set; }
    [Range(120, 230)] public int? PartnerHeightMin { get; set; }
    [Range(120, 230)] public int? PartnerHeightMax { get; set; }
    public List<string>? PartnerMarital { get; set; }
    public List<string>? PartnerEducation { get; set; }
    public List<string>? PartnerEmployment { get; set; }
    [StringLength(40)] public string? PartnerIncome { get; set; }
    [MaxLength(30)] public List<string>? PartnerLocations { get; set; }
    [StringLength(2000)] public string? PartnerExpectations { get; set; }
}

public sealed record MyProfileResponse(string MemberId, MyProfile Profile, ProfileCompletion Completion, DateTime UpdatedAt);

public sealed record PhotoUploadResponse(string Url, List<string> Photos);

public sealed record PhotosResponse(List<string> Photos);

public sealed class DeletePhotoRequest
{
    [Required] public string? Url { get; set; }
}

/// <summary>multipart/form-data body for P8.</summary>
public sealed class KycSubmitForm
{
    /// <example>aadhaar</example>
    [Required, AllowedValues("aadhaar", "passport", "pan", "driving_license")]
    public string? DocumentType { get; set; }

    [Required] public IFormFile? DocumentFile { get; set; }
    public IFormFile? DocumentBackFile { get; set; }
    [Required] public IFormFile? SelfieFile { get; set; }
    public IFormFile? VideoIntroFile { get; set; }
}

public sealed record VerificationResponse(string Status, string? DocumentType, bool SelfieUploaded, DateTime? SubmittedAt);

/// <summary>Dev-only: result of GET /v1/dev/db-check.</summary>
public sealed record DbCheckResponse(
    bool Connected, string Server, string Database, Dictionary<string, int> RowCounts,
    List<string> AppliedMigrations, List<string> PendingMigrations);

/// <summary>Dev-only: send a test message through the configured provider.</summary>
public sealed class DevTestDeliveryRequest
{
    /// <example>email</example>
    [AllowedValues("sms", "whatsapp", "email")] public string Channel { get; set; } = "sms";

    /// <summary>+91XXXXXXXXXX for sms/whatsapp, an email address for email.</summary>
    /// <example>you@example.com</example>
    [Required] public string? To { get; set; }
}

public sealed record DevTestDeliveryResponse(bool Sent, string Provider, string? ProviderMessageId, string Note);

/// <summary>Dev-only helper to approve/reject KYC while testing.</summary>
public sealed class DevKycReviewRequest
{
    [Required, AllowedValues("verified", "rejected")] public string? Status { get; set; }
    public string? Reason { get; set; }
}
