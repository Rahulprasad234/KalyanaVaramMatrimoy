/* =====================================================================
   KalyanaVaran - Get OTP -> Verify -> Token : SQL used by each API step
   ---------------------------------------------------------------------
   These are the T-SQL statements EF Core runs for the auth flow, written by
   hand so DBAs / backend devs can follow, test or port them.

   Hashing (OTP HMAC-SHA256, refresh-token SHA-256) is done in C#, not SQL:
   the database never sees a plain OTP or refresh token.

   Each section is independent: set the variables at the top and run it.
   ===================================================================== */
USE KalyanaVaran;
GO

/* =====================================================================
   A1  POST /v1/auth/register   (UI: /login  "Get OTP & Start Profile")
   ===================================================================== */
DECLARE @ProfileFor NVARCHAR(10)  = N'myself',
        @FullName   NVARCHAR(120) = N'Radhika Sharma',
        @Gender     NVARCHAR(6)   = N'female',
        @Mobile     NVARCHAR(15)  = N'+919876543210',
        @Email      NVARCHAR(255) = N'family@example.com',
        @MemberId   NVARCHAR(12)  = N'DM-984210',          -- generated in C#, checked for uniqueness below
        @Now        DATETIME2     = SYSUTCDATETIME();

-- 1) Already registered?  Active row => 409 CONFLICT "This mobile number is already registered. Please log in."
SELECT Id, Mobile, Email, Status
FROM dbo.Users
WHERE Mobile = @Mobile OR Email = @Email;

-- 2) Member id must be unique (retry with a new random id if this returns 1)
SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.Users WHERE MemberId = @MemberId) THEN 1 ELSE 0 END AS MemberIdTaken;

-- 3) Remove an abandoned (never verified) registration that holds this email under another mobile
DELETE FROM dbo.Users
WHERE Email = @Email AND Mobile <> @Mobile AND Status = N'pending';

-- 4) Create the pending user, or refresh an earlier pending registration for this mobile
IF EXISTS (SELECT 1 FROM dbo.Users WHERE Mobile = @Mobile AND Status = N'pending')
    UPDATE dbo.Users
    SET ProfileFor = @ProfileFor, FullName = @FullName, Gender = @Gender, Email = @Email,
        TermsAcceptedAt = @Now, UpdatedAt = @Now
    WHERE Mobile = @Mobile AND Status = N'pending';
ELSE
    INSERT INTO dbo.Users (MemberId, ProfileFor, FullName, Gender, Mobile, Email, MobileVerified, EmailVerified,
                           Status, OnboardingStep, TermsAcceptedAt, CreatedAt, UpdatedAt)
    VALUES (@MemberId, @ProfileFor, @FullName, @Gender, @Mobile, @Email, 0, 0,
            N'pending', 1, @Now, @Now, @Now);

-- 5) Send OTPs -> run section "SEND OTP" below for Channel = 'sms' and then Channel = 'email'
GO

/* =====================================================================
   SEND OTP  (used by A1 register and A2 /v1/auth/otp/send)
   Phone codes (sms/whatsapp) and email codes have separate limits.
   ===================================================================== */
DECLARE @Mobile      NVARCHAR(15)  = N'+919876543210',
        @Channel     NVARCHAR(10)  = N'sms',                -- sms | whatsapp | email
        @Purpose     NVARCHAR(10)  = N'register',           -- register (pending user) | login (active user)
        @OtpId       NVARCHAR(20)  = N'otp_7f3a9c21ab12',   -- generated in C#
        @OtpHash     NVARCHAR(100) = N'<HMAC hex from C#>',
        @Ip          NVARCHAR(45)  = N'127.0.0.1',
        @Now         DATETIME2     = SYSUTCDATETIME();

DECLARE @IsEmail BIT = CASE WHEN @Channel = N'email' THEN 1 ELSE 0 END;

-- 1) The user the code belongs to (A2: none => 404 "No account found for this mobile number. Please register first.")
DECLARE @UserId BIGINT, @Email NVARCHAR(255), @Status NVARCHAR(10);
SELECT @UserId = Id, @Email = Email, @Status = Status
FROM dbo.Users WHERE Mobile = @Mobile;
-- Status = 'blocked' => 403 FORBIDDEN

-- 2) Rate limits for this kind of code (last hour)
SELECT TOP (1)
       ResendAfter,                                                      -- > now  => 429 "Please wait N seconds..."
       (SELECT COUNT(*) FROM dbo.OtpRequests r
         WHERE r.Mobile = @Mobile AND r.CreatedAt > DATEADD(HOUR, -1, @Now)
           AND ((@IsEmail = 1 AND r.Channel = N'email') OR (@IsEmail = 0 AND r.Channel <> N'email'))
       ) AS SentLastHour                                                 -- >= 5   => 429 "Too many OTP requests..."
FROM dbo.OtpRequests
WHERE Mobile = @Mobile AND CreatedAt > DATEADD(HOUR, -1, @Now)
  AND ((@IsEmail = 1 AND Channel = N'email') OR (@IsEmail = 0 AND Channel <> N'email'))
ORDER BY CreatedAt DESC;

-- 3) Only the newest code stays valid: retire older unused codes of the same kind
UPDATE dbo.OtpRequests
SET ConsumedAt = @Now
WHERE Mobile = @Mobile AND ConsumedAt IS NULL
  AND ((@IsEmail = 1 AND Channel = N'email') OR (@IsEmail = 0 AND Channel <> N'email'));

-- 4) Store the new code (10 min validity, 30 s resend cooldown)
INSERT INTO dbo.OtpRequests (Id, UserId, Mobile, Channel, Destination, Purpose, OtpHash, Attempts,
                             ExpiresAt, ResendAfter, ConsumedAt, Ip, CreatedAt)
VALUES (@OtpId, @UserId, @Mobile, @Channel,
        CASE WHEN @IsEmail = 1 THEN @Email ELSE @Mobile END,
        @Purpose, @OtpHash, 0,
        DATEADD(SECOND, 600, @Now), DATEADD(SECOND, 30, @Now), NULL, @Ip, @Now);

-- API response data: { otpRequestId: @OtpId, mobile: @Mobile, channel: @Channel, expiresInSeconds: 600, resendAfterSeconds: 30 }
GO

/* =====================================================================
   A3  POST /v1/auth/otp/verify   (UI: /otp  "Continue")
   ===================================================================== */
DECLARE @Mobile NVARCHAR(15) = N'+919876543210',
        @Now    DATETIME2    = SYSUTCDATETIME();

-- 1) Newest unused mobile code (works even if the UI still holds an older otpRequestId after "Resend")
SELECT TOP (1) Id, UserId, OtpHash, Attempts, ExpiresAt
FROM dbo.OtpRequests
WHERE Mobile = @Mobile AND Channel <> N'email' AND ConsumedAt IS NULL
ORDER BY CreatedAt DESC;
--   none            => 400 OTP_EXPIRED / OTP_INVALID
--   Attempts >= 5   => 429 OTP_RATE_LIMITED
--   ExpiresAt <= now => 400 OTP_EXPIRED
--   C# compares HMAC(Id:otp) with OtpHash (constant-time)

-- 2) Only if the app sent emailOtp: newest unused email code
SELECT TOP (1) Id, OtpHash, Attempts, ExpiresAt
FROM dbo.OtpRequests
WHERE Mobile = @Mobile AND Channel = N'email' AND ConsumedAt IS NULL
ORDER BY CreatedAt DESC;

-- 3a) Wrong code => count the attempt, return 400 OTP_INVALID "... N attempts left."
DECLARE @OtpId NVARCHAR(20) = N'otp_7f3a9c21ab12';
UPDATE dbo.OtpRequests SET Attempts = Attempts + 1 WHERE Id = @OtpId;

-- 3b) Correct => consume the code(s), activate the user, create the profile row, issue tokens
DECLARE @PhoneOtpId NVARCHAR(20) = N'otp_7f3a9c21ab12',
        @EmailOtpId NVARCHAR(20) = NULL,                    -- set when the email code was verified too
        @UserId     BIGINT,
        @RefreshHash NVARCHAR(64) = N'<SHA-256 hex of the refresh token, from C#>',
        @UserAgent  NVARCHAR(300) = N'Mozilla/5.0 ...',
        @Ip         NVARCHAR(45)  = N'127.0.0.1';

SELECT @UserId = UserId FROM dbo.OtpRequests WHERE Id = @PhoneOtpId;

BEGIN TRANSACTION;

    UPDATE dbo.OtpRequests SET ConsumedAt = @Now WHERE Id IN (@PhoneOtpId, @EmailOtpId);

    DECLARE @IsNewUser BIT = CASE WHEN EXISTS (SELECT 1 FROM dbo.Users WHERE Id = @UserId AND Status = N'pending') THEN 1 ELSE 0 END;

    UPDATE dbo.Users
    SET Status         = CASE WHEN Status = N'pending' THEN N'active' ELSE Status END,
        MobileVerified = 1,
        EmailVerified  = CASE WHEN @EmailOtpId IS NOT NULL THEN 1 ELSE EmailVerified END,
        LastLoginAt    = @Now,
        UpdatedAt      = @Now
    WHERE Id = @UserId;

    -- First login: create the empty profile, splitting FullName into first / last name
    IF NOT EXISTS (SELECT 1 FROM dbo.Profiles WHERE UserId = @UserId)
        INSERT INTO dbo.Profiles (UserId, FirstName, LastName, UpdatedAt)
        SELECT Id,
               LEFT(LTRIM(FullName), CHARINDEX(N' ', LTRIM(FullName) + N' ') - 1),
               LTRIM(SUBSTRING(LTRIM(FullName), CHARINDEX(N' ', LTRIM(FullName) + N' ') + 1, 120)),
               @Now
        FROM dbo.Users WHERE Id = @UserId;

    -- Refresh token (30 days). The access token is a JWT signed in C#, it is not stored.
    INSERT INTO dbo.RefreshTokens (UserId, TokenHash, ExpiresAt, UserAgent, Ip, CreatedAt)
    VALUES (@UserId, @RefreshHash, DATEADD(DAY, 30, @Now), @UserAgent, @Ip, @Now);

COMMIT TRANSACTION;

-- 4) "user" block of the AuthSession response
SELECT MemberId       AS memberId,
       FullName       AS fullName,
       Mobile         AS mobile,
       Email          AS email,
       Gender         AS gender,
       ProfileFor     AS profileFor,
       MobileVerified AS mobileVerified,
       EmailVerified  AS emailVerified,
       @IsNewUser     AS isNewUser,
       OnboardingStep AS onboardingStep        -- 1..6 = next wizard step, 7 = finished
FROM dbo.Users WHERE Id = @UserId;
GO

/* =====================================================================
   A5  POST /v1/auth/refresh   (rotate refresh token)
   ===================================================================== */
DECLARE @OldHash NVARCHAR(64) = N'<SHA-256 hex of incoming refresh token>',
        @NewHash NVARCHAR(64) = N'<SHA-256 hex of new refresh token>',
        @Now     DATETIME2    = SYSUTCDATETIME();

DECLARE @TokenId BIGINT, @UserId BIGINT, @RevokedAt DATETIME2, @ExpiresAt DATETIME2;
SELECT @TokenId = Id, @UserId = UserId, @RevokedAt = RevokedAt, @ExpiresAt = ExpiresAt
FROM dbo.RefreshTokens WHERE TokenHash = @OldHash;
--   not found / expired / user not active => 401 UNAUTHORIZED

-- Reuse of an already-rotated token = probable theft: revoke every session of the user, 401
IF @RevokedAt IS NOT NULL
    UPDATE dbo.RefreshTokens SET RevokedAt = @Now WHERE UserId = @UserId AND RevokedAt IS NULL;
ELSE IF @ExpiresAt > @Now
BEGIN
    BEGIN TRANSACTION;
        INSERT INTO dbo.RefreshTokens (UserId, TokenHash, ExpiresAt, CreatedAt)
        VALUES (@UserId, @NewHash, DATEADD(DAY, 30, @Now), @Now);

        UPDATE dbo.RefreshTokens
        SET RevokedAt = @Now, ReplacedById = SCOPE_IDENTITY()
        WHERE Id = @TokenId;
    COMMIT TRANSACTION;
END
GO

/* =====================================================================
   A6  POST /v1/auth/logout   (revoke all sessions of the user)
   ===================================================================== */
DECLARE @UserId BIGINT = 1;
UPDATE dbo.RefreshTokens SET RevokedAt = SYSUTCDATETIME()
WHERE UserId = @UserId AND RevokedAt IS NULL;
GO

/* =====================================================================
   Middleware: ActiveUserMiddleware (cached 60 s)
   ===================================================================== */
DECLARE @UserId BIGINT = 1;
SELECT Status FROM dbo.Users WHERE Id = @UserId;   -- NULL => 401, 'blocked' => 403
GO

/* =====================================================================
   Support / testing queries
   ===================================================================== */
-- Latest OTPs for a number (codes are hashed, never visible)
SELECT TOP (10) Id, Channel, Destination, Purpose, Attempts, CreatedAt, ExpiresAt, ResendAfter, ConsumedAt
FROM dbo.OtpRequests
WHERE Mobile = N'+919876543210'
ORDER BY CreatedAt DESC;

-- Registrations that never verified the OTP
SELECT MemberId, FullName, Mobile, Email, CreatedAt
FROM dbo.Users WHERE Status = N'pending'
ORDER BY CreatedAt DESC;

-- Active sessions per user
SELECT u.MemberId, u.FullName, COUNT(*) AS ActiveSessions, MAX(t.CreatedAt) AS LastLogin
FROM dbo.RefreshTokens t JOIN dbo.Users u ON u.Id = t.UserId
WHERE t.RevokedAt IS NULL AND t.ExpiresAt > SYSUTCDATETIME()
GROUP BY u.MemberId, u.FullName;

-- Block a member (ActiveUserMiddleware applies it within 60 s; also revoke sessions)
-- UPDATE dbo.Users SET Status = N'blocked', UpdatedAt = SYSUTCDATETIME() WHERE MemberId = N'DM-984210';
-- UPDATE dbo.RefreshTokens SET RevokedAt = SYSUTCDATETIME() WHERE UserId = (SELECT Id FROM dbo.Users WHERE MemberId = N'DM-984210') AND RevokedAt IS NULL;
GO

/* =====================================================================
   Nightly cleanup job (SQL Agent): keep tables small
   ===================================================================== */
DELETE FROM dbo.OtpRequests  WHERE CreatedAt < DATEADD(DAY, -7, SYSUTCDATETIME());
DELETE FROM dbo.RefreshTokens WHERE (RevokedAt IS NOT NULL OR ExpiresAt < SYSUTCDATETIME())
                               AND CreatedAt < DATEADD(DAY, -30, SYSUTCDATETIME());
-- Abandoned registrations older than 7 days (cascades to their OTP rows)
DELETE FROM dbo.Users WHERE Status = N'pending' AND CreatedAt < DATEADD(DAY, -7, SYSUTCDATETIME());
GO
