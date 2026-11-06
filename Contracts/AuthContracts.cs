using System.ComponentModel.DataAnnotations;

namespace KalyanaVaran.Api.Contracts;

public static class ValidationPatterns
{
    // The UI strips non-digits and sends "+91" + 10 digits (or "+" + digits if the user typed a country code)
    public const string IndianMobile = @"^\+91[6-9]\d{9}$";
    public const string MobileMessage = "Please enter a valid 10-digit Indian mobile number.";
}

// A1 - /login "Get OTP & Start Profile"
public sealed class RegisterRequest
{
    /// <example>myself</example>
    [Required(ErrorMessage = "Please choose who this profile is for.")]
    [AllowedValues("myself", "child", "sibling", "relative", ErrorMessage = "Please choose who this profile is for.")]
    public string? ProfileFor { get; set; }

    /// <example>Radhika Sharma</example>
    [Required(ErrorMessage = "Please enter the bride / groom's full name.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "Full name must be between 2 and 120 characters.")]
    public string? FullName { get; set; }

    /// <example>female</example>
    [Required(ErrorMessage = "Please select a gender.")]
    [AllowedValues("male", "female", ErrorMessage = "Please select a gender.")]
    public string? Gender { get; set; }

    /// <example>+919876543210</example>
    [Required(ErrorMessage = ValidationPatterns.MobileMessage)]
    [RegularExpression(ValidationPatterns.IndianMobile, ErrorMessage = ValidationPatterns.MobileMessage)]
    public string? Mobile { get; set; }

    /// <example>family@example.com</example>
    [Required(ErrorMessage = "Please enter an email address.")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    [StringLength(255)]
    public string? Email { get; set; }

    /// <example>true</example>
    [Required(ErrorMessage = "Please accept the Terms of Use and Privacy Policy to continue.")]
    [Range(typeof(bool), "true", "true", ErrorMessage = "Please accept the Terms of Use and Privacy Policy to continue.")]
    public bool? AcceptedTerms { get; set; }
}

// A2 - "Resend OTP" (sms / email) and existing-member login
public sealed class SendOtpRequest
{
    /// <example>+919876543210</example>
    [Required(ErrorMessage = ValidationPatterns.MobileMessage)]
    [RegularExpression(ValidationPatterns.IndianMobile, ErrorMessage = ValidationPatterns.MobileMessage)]
    public string? Mobile { get; set; }

    /// <summary>sms | whatsapp | email (email resends the code to the registered email).</summary>
    /// <example>sms</example>
    [AllowedValues("sms", "whatsapp", "email", ErrorMessage = "Channel must be sms, whatsapp or email.")]
    public string Channel { get; set; } = "sms";
}

// A3 - /otp "Continue"
public sealed class VerifyOtpRequest
{
    /// <example>otp_7f3a9c21ab12</example>
    [Required(ErrorMessage = "This OTP request is not valid. Please request a new OTP.")]
    [StringLength(20)]
    public string? OtpRequestId { get; set; }

    /// <example>+919876543210</example>
    [Required(ErrorMessage = ValidationPatterns.MobileMessage)]
    [RegularExpression(ValidationPatterns.IndianMobile, ErrorMessage = ValidationPatterns.MobileMessage)]
    public string? Mobile { get; set; }

    /// <summary>Mobile OTP (required).</summary>
    /// <example>842910</example>
    [Required(ErrorMessage = "Please enter the 6-digit mobile OTP.")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "Please enter the 6-digit mobile OTP.")]
    public string? Otp { get; set; }

    /// <summary>Email OTP (optional). When sent and correct, the email is marked verified.</summary>
    /// <example>842910</example>
    [RegularExpression(@"^\d{6}$", ErrorMessage = "Please enter the 6-digit email OTP.")]
    public string? EmailOtp { get; set; }
}

// A5
public sealed class RefreshRequest
{
    [Required(ErrorMessage = "Your session has expired. Please log in again.")]
    public string? RefreshToken { get; set; }
}

public sealed record OtpSentResponse(
    string OtpRequestId, string Mobile, string Channel, int ExpiresInSeconds, int ResendAfterSeconds);

public sealed record AuthTokens(string AccessToken, string RefreshToken, string TokenType, int ExpiresIn);

public sealed record AuthUser(
    string MemberId, string FullName, string? Mobile, string? Email, string Gender, string ProfileFor,
    bool MobileVerified, bool EmailVerified, bool IsNewUser, int OnboardingStep);

public sealed record AuthSession(AuthTokens Tokens, AuthUser User);

public sealed record LogoutResponse(bool LoggedOut);
