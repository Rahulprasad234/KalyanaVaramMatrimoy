namespace KalyanaVaran.Api.Entities;

public static class UserStatus
{
    public const string Pending = "pending";   // registered, mobile not verified yet
    public const string Active = "active";
    public const string Blocked = "blocked";
}

public static class KycStatus
{
    public const string NotSubmitted = "not_submitted";
    public const string Pending = "pending";
    public const string Verified = "verified";
    public const string Rejected = "rejected";
}

public class User
{
    public long Id { get; set; }
    public string MemberId { get; set; } = "";          // DM-984210
    public string ProfileFor { get; set; } = "";        // myself | child | sibling | relative
    public string FullName { get; set; } = "";
    public string Gender { get; set; } = "";            // male | female
    public string? Mobile { get; set; }                 // E.164
    public string? Email { get; set; }
    public string? GoogleSub { get; set; }
    public bool MobileVerified { get; set; }
    public bool EmailVerified { get; set; }
    public string Status { get; set; } = UserStatus.Pending;
    public short OnboardingStep { get; set; } = 1;      // 1..6, 7 = finished
    public DateTime? TermsAcceptedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Profile? Profile { get; set; }
    public PartnerPreference? PartnerPreference { get; set; }
    public List<Sibling> Siblings { get; set; } = [];
    public List<Photo> Photos { get; set; } = [];
}

public class OtpRequest
{
    public string Id { get; set; } = "";                // otp_7f3a9c21ab
    public long UserId { get; set; }
    public string Mobile { get; set; } = "";            // always the user's mobile (lookup key)
    public string Channel { get; set; } = "sms";        // sms | whatsapp | email
    public string Destination { get; set; } = "";       // where the code went: mobile or email address
    public string Purpose { get; set; } = "login";      // register | login
    public string OtpHash { get; set; } = "";
    public short Attempts { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime ResendAfter { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime? VerifiedAt { get; set; }           // set only when this code was entered correctly
    public string? ProviderMessageId { get; set; }      // MSG91 / Twilio / SendGrid / SMTP message id (for support)
    public string? Ip { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class RefreshToken
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public long? ReplacedById { get; set; }
    public string? UserAgent { get; set; }
    public string? Ip { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Raw JSON of each wizard step, returned as-is by GET /onboarding/{step}.</summary>
public class OnboardingStepRecord
{
    public long UserId { get; set; }
    public short Step { get; set; }
    public string Data { get; set; } = "{}";            // JSON (nvarchar(max))
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}

public class Profile
{
    public long UserId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    // Step 1
    public DateOnly? DateOfBirth { get; set; }
    public string? MaritalStatus { get; set; }
    public short? HeightCm { get; set; }
    public short? WeightKg { get; set; }
    public string? BodyType { get; set; }
    public string? Complexion { get; set; }
    public bool? HasDisability { get; set; }

    // Step 2
    public string? Religion { get; set; }
    public string? OtherReligion { get; set; }
    public string? Caste { get; set; }
    public string? OtherCaste { get; set; }
    public string? SubCaste { get; set; }
    public string? Gothram { get; set; }
    public string? Nakshatra { get; set; }
    public string? Rasi { get; set; }
    public string? Dosham { get; set; }
    public TimeOnly? BirthTime { get; set; }
    public string? BirthPlace { get; set; }
    public bool AutoGenerateChart { get; set; }
    public List<string> HoroscopeUrls { get; set; } = [];

    // Step 3
    public string? Education { get; set; }
    public string? OtherEducation { get; set; }
    public string? College { get; set; }
    public string? EmploymentSector { get; set; }
    public string? Profession { get; set; }
    public string? Employer { get; set; }
    public string? AnnualIncome { get; set; }
    public string? IncomeBand { get; set; }
    public string? WorkLocation { get; set; }
    public bool? OpenToRelocate { get; set; }

    // Step 4
    public string? FamilyType { get; set; }
    public string? FamilyValues { get; set; }
    public string? FatherOccupation { get; set; }
    public string? MotherOccupation { get; set; }
    public string? Diet { get; set; }
    public string? Smoking { get; set; }
    public string? Drinking { get; set; }
    public List<string> Hobbies { get; set; } = [];
    public string? CustomHobbies { get; set; }          // JSON (nvarchar(max)) [{name, icon}]
    public List<string> Interests { get; set; } = [];
    public string? About { get; set; }

    // Step 5
    public bool PhotoBlur { get; set; }
    public string? IdType { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class Sibling
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Relation { get; set; } = "";          // brother | sister
    public string? ClientId { get; set; }               // id sent by the UI
    public string Name { get; set; } = "";
    public string? Age { get; set; }
    public string? MaritalStatus { get; set; }          // Married | Unmarried
    public short SortOrder { get; set; }
}

public class PartnerPreference
{
    public long UserId { get; set; }
    public int? AgeMin { get; set; }
    public int? AgeMax { get; set; }
    public int? HeightMinCm { get; set; }
    public int? HeightMaxCm { get; set; }
    public List<string> Marital { get; set; } = [];
    public List<string> Languages { get; set; } = [];
    public bool AnyMotherTongue { get; set; }
    public bool InterCaste { get; set; }
    public string? Religion { get; set; }
    public string? Caste { get; set; }
    public string? OtherCaste { get; set; }
    public string? SubCaste { get; set; }
    public string? OtherSubCaste { get; set; }
    public bool JathagamMatch { get; set; }
    public string? Dosham { get; set; }
    public List<string> Education { get; set; } = [];
    public List<string> Employment { get; set; } = [];
    public string? Income { get; set; }
    public List<string> Locations { get; set; } = [];
    public string? CustomCities { get; set; }           // JSON (nvarchar(max)) [{name, icon}]
    public bool OpenToRelocate { get; set; }
    public string? Expectations { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class Photo
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string StorageKey { get; set; } = "";
    public string Url { get; set; } = "";
    public short SortOrder { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }
}

public class KycVerification
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string DocumentType { get; set; } = "";      // aadhaar | passport | pan | driving_license
    public string DocFrontKey { get; set; } = "";
    public string? DocBackKey { get; set; }
    public string SelfieKey { get; set; } = "";
    public string? VideoKey { get; set; }
    public string Status { get; set; } = KycStatus.Pending;
    public string? RejectionReason { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
}
