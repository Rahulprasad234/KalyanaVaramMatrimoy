/* =====================================================================
   KalyanaVaram - table script for AZURE SQL DATABASE
   Server  : aspireright.database.windows.net
   Database: KalyanaVaram

   HOW TO RUN (SSMS):
     1. In the database dropdown on the toolbar, select  KalyanaVaram
     2. Open this file (or paste it) and press F5
   Safe to run again and again: existing tables are skipped, and missing
   columns from newer versions are added (see "UPGRADE" section).
   Azure SQL note: CREATE DATABASE / USE are not used on purpose.
   ===================================================================== */

IF DB_NAME() = N'master'
    THROW 50000, N'Select the KalyanaVaram database in the toolbar dropdown first, then run again.', 1;
GO

/* ---------------------------------------------------------------------
   1. Users - one row per member account
   --------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users
    (
        Id               BIGINT          IDENTITY(1,1) NOT NULL,
        MemberId         NVARCHAR(12)    NOT NULL,                       -- DM-984210
        ProfileFor       NVARCHAR(10)    NOT NULL,                       -- myself | child | sibling | relative
        FullName         NVARCHAR(120)   NOT NULL,
        Gender           NVARCHAR(6)     NOT NULL,                       -- male | female
        Mobile           NVARCHAR(15)    NULL,                           -- E.164 +919876543210
        Email            NVARCHAR(255)   NULL,
        GoogleSub        NVARCHAR(64)    NULL,                           -- Google account id (future)
        MobileVerified   BIT             NOT NULL CONSTRAINT DF_Users_MobileVerified DEFAULT (0),
        EmailVerified    BIT             NOT NULL CONSTRAINT DF_Users_EmailVerified  DEFAULT (0),
        Status           NVARCHAR(10)    NOT NULL CONSTRAINT DF_Users_Status         DEFAULT (N'pending'),
        OnboardingStep   SMALLINT        NOT NULL CONSTRAINT DF_Users_OnboardingStep DEFAULT (1),  -- 1..6, 7 = done
        TermsAcceptedAt  DATETIME2       NULL,
        LastLoginAt      DATETIME2       NULL,
        CreatedAt        DATETIME2       NOT NULL CONSTRAINT DF_Users_CreatedAt      DEFAULT (SYSUTCDATETIME()),
        UpdatedAt        DATETIME2       NOT NULL CONSTRAINT DF_Users_UpdatedAt      DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_Users PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_Users_ProfileFor     CHECK (ProfileFor IN (N'myself', N'child', N'sibling', N'relative')),
        CONSTRAINT CK_Users_Gender         CHECK (Gender IN (N'male', N'female')),
        CONSTRAINT CK_Users_Status         CHECK (Status IN (N'pending', N'active', N'blocked')),
        CONSTRAINT CK_Users_OnboardingStep CHECK (OnboardingStep BETWEEN 1 AND 7)
    );

    CREATE UNIQUE INDEX IX_Users_MemberId  ON dbo.Users (MemberId);
    -- Filtered: many rows may have NULL (e.g. Google sign-up without mobile)
    CREATE UNIQUE INDEX IX_Users_Mobile    ON dbo.Users (Mobile)    WHERE Mobile    IS NOT NULL;
    CREATE UNIQUE INDEX IX_Users_Email     ON dbo.Users (Email)     WHERE Email     IS NOT NULL;
    CREATE UNIQUE INDEX IX_Users_GoogleSub ON dbo.Users (GoogleSub) WHERE GoogleSub IS NOT NULL;
END
GO

/* ---------------------------------------------------------------------
   2. OtpRequests - every OTP sent (code stored only as HMAC hash)
   --------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.OtpRequests', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OtpRequests
    (
        Id           NVARCHAR(20)   NOT NULL,                            -- otp_7f3a9c21ab12
        UserId       BIGINT         NOT NULL,
        Mobile       NVARCHAR(15)   NOT NULL,
        Channel      NVARCHAR(10)   NOT NULL,                            -- sms | whatsapp | email
        Destination  NVARCHAR(255)  NOT NULL,                            -- mobile number or email address the code went to
        Purpose      NVARCHAR(10)   NOT NULL,                            -- register | login
        OtpHash      NVARCHAR(100)  NOT NULL,
        Attempts     SMALLINT       NOT NULL CONSTRAINT DF_OtpRequests_Attempts DEFAULT (0),
        ExpiresAt    DATETIME2      NOT NULL,                            -- created + 10 min
        ResendAfter  DATETIME2      NOT NULL,                            -- created + 30 s
        ConsumedAt   DATETIME2      NULL,                                -- used or superseded
        Ip           NVARCHAR(45)   NULL,
        CreatedAt    DATETIME2      NOT NULL CONSTRAINT DF_OtpRequests_CreatedAt DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_OtpRequests PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_OtpRequests_Users_UserId FOREIGN KEY (UserId)
            REFERENCES dbo.Users (Id) ON DELETE CASCADE,
        CONSTRAINT CK_OtpRequests_Channel CHECK (Channel IN (N'sms', N'whatsapp', N'email')),
        CONSTRAINT CK_OtpRequests_Purpose CHECK (Purpose IN (N'register', N'login'))
    );

    CREATE INDEX IX_OtpRequests_Mobile_Channel_CreatedAt ON dbo.OtpRequests (Mobile, Channel, CreatedAt);  -- latest code + rate limits
    CREATE INDEX IX_OtpRequests_UserId                   ON dbo.OtpRequests (UserId);
END
GO

/* ---------------------------------------------------------------------
   UPGRADE: columns/indexes added after the first version
   (only runs when something is missing; safe on a fresh database)
   --------------------------------------------------------------------- */
IF COL_LENGTH(N'dbo.OtpRequests', N'Destination') IS NULL
    ALTER TABLE dbo.OtpRequests ADD Destination NVARCHAR(255) NOT NULL
        CONSTRAINT DF_OtpRequests_Destination DEFAULT (N'');
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_OtpRequests_Channel'
           AND definition NOT LIKE N'%email%')
BEGIN
    ALTER TABLE dbo.OtpRequests DROP CONSTRAINT CK_OtpRequests_Channel;
    ALTER TABLE dbo.OtpRequests ADD CONSTRAINT CK_OtpRequests_Channel
        CHECK (Channel IN (N'sms', N'whatsapp', N'email'));
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OtpRequests_Mobile_CreatedAt'
           AND object_id = OBJECT_ID(N'dbo.OtpRequests'))
    DROP INDEX IX_OtpRequests_Mobile_CreatedAt ON dbo.OtpRequests;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OtpRequests_Mobile_Channel_CreatedAt'
               AND object_id = OBJECT_ID(N'dbo.OtpRequests'))
    CREATE INDEX IX_OtpRequests_Mobile_Channel_CreatedAt ON dbo.OtpRequests (Mobile, Channel, CreatedAt);
GO

/* ---------------------------------------------------------------------
   3. RefreshTokens - login sessions (token stored only as SHA-256 hash)
   --------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.RefreshTokens', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RefreshTokens
    (
        Id            BIGINT         IDENTITY(1,1) NOT NULL,
        UserId        BIGINT         NOT NULL,
        TokenHash     NVARCHAR(64)   NOT NULL,
        ExpiresAt     DATETIME2      NOT NULL,                           -- created + 30 days
        RevokedAt     DATETIME2      NULL,                               -- logout / rotation
        ReplacedById  BIGINT         NULL,                               -- next token after rotation
        UserAgent     NVARCHAR(300)  NULL,
        Ip            NVARCHAR(45)   NULL,
        CreatedAt     DATETIME2      NOT NULL CONSTRAINT DF_RefreshTokens_CreatedAt DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_RefreshTokens PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_RefreshTokens_Users_UserId FOREIGN KEY (UserId)
            REFERENCES dbo.Users (Id) ON DELETE CASCADE
    );

    CREATE UNIQUE INDEX IX_RefreshTokens_TokenHash ON dbo.RefreshTokens (TokenHash);
    CREATE INDEX        IX_RefreshTokens_UserId    ON dbo.RefreshTokens (UserId);
END
GO

/* ---------------------------------------------------------------------
   4. OnboardingSteps - raw JSON of each wizard step (GET /onboarding/{step})
   --------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.OnboardingSteps', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OnboardingSteps
    (
        UserId       BIGINT         NOT NULL,
        Step         SMALLINT       NOT NULL,                            -- 1..6
        Data         NVARCHAR(MAX)  NOT NULL,                            -- JSON exactly as the app sent it
        CompletedAt  DATETIME2      NOT NULL CONSTRAINT DF_OnboardingSteps_CompletedAt DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_OnboardingSteps PRIMARY KEY CLUSTERED (UserId, Step),
        CONSTRAINT FK_OnboardingSteps_Users_UserId FOREIGN KEY (UserId)
            REFERENCES dbo.Users (Id) ON DELETE CASCADE,
        CONSTRAINT CK_OnboardingSteps_Step   CHECK (Step BETWEEN 1 AND 6),
        CONSTRAINT CK_OnboardingSteps_IsJson CHECK (ISJSON(Data) = 1)
    );
END
GO

/* ---------------------------------------------------------------------
   5. Profiles - one row per user; wizard steps 1-5 mapped into columns
      List columns (HoroscopeUrls, Hobbies, Interests) hold JSON arrays: ["Music","Travel"]
   --------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.Profiles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Profiles
    (
        UserId             BIGINT          NOT NULL,
        FirstName          NVARCHAR(200)   NULL,
        LastName           NVARCHAR(200)   NULL,

        -- Step 1: basic & physical
        DateOfBirth        DATE            NULL,
        MaritalStatus      NVARCHAR(200)   NULL,                         -- Never Married | Divorced | Widowed | Awaiting Divorce
        HeightCm           SMALLINT        NULL,
        WeightKg           SMALLINT        NULL,
        BodyType           NVARCHAR(200)   NULL,
        Complexion         NVARCHAR(200)   NULL,
        HasDisability      BIT             NULL,

        -- Step 2: religion & jathagam
        Religion           NVARCHAR(200)   NULL,
        OtherReligion      NVARCHAR(200)   NULL,
        Caste              NVARCHAR(200)   NULL,
        OtherCaste         NVARCHAR(200)   NULL,
        SubCaste           NVARCHAR(200)   NULL,
        Gothram            NVARCHAR(200)   NULL,
        Nakshatra          NVARCHAR(200)   NULL,
        Rasi               NVARCHAR(200)   NULL,
        Dosham             NVARCHAR(200)   NULL,
        BirthTime          TIME(7)         NULL,
        BirthPlace         NVARCHAR(200)   NULL,
        AutoGenerateChart  BIT             NOT NULL CONSTRAINT DF_Profiles_AutoGenerateChart DEFAULT (0),
        HoroscopeUrls      NVARCHAR(MAX)   NOT NULL CONSTRAINT DF_Profiles_HoroscopeUrls     DEFAULT (N'[]'),

        -- Step 3: education & career
        Education          NVARCHAR(200)   NULL,
        OtherEducation     NVARCHAR(200)   NULL,
        College            NVARCHAR(200)   NULL,
        EmploymentSector   NVARCHAR(200)   NULL,
        Profession         NVARCHAR(200)   NULL,
        Employer           NVARCHAR(200)   NULL,
        AnnualIncome       NVARCHAR(200)   NULL,                         -- label, e.g. "₹18 Lakhs - ₹25 Lakhs per annum"
        IncomeBand         NVARCHAR(200)   NULL,                         -- chip, e.g. "₹18 - 25L"
        WorkLocation       NVARCHAR(200)   NULL,
        OpenToRelocate     BIT             NULL,

        -- Step 4: family & lifestyle
        FamilyType         NVARCHAR(200)   NULL,
        FamilyValues       NVARCHAR(200)   NULL,
        FatherOccupation   NVARCHAR(200)   NULL,
        MotherOccupation   NVARCHAR(200)   NULL,
        Diet               NVARCHAR(200)   NULL,
        Smoking            NVARCHAR(200)   NULL,
        Drinking           NVARCHAR(200)   NULL,
        Hobbies            NVARCHAR(MAX)   NOT NULL CONSTRAINT DF_Profiles_Hobbies   DEFAULT (N'[]'),
        CustomHobbies      NVARCHAR(MAX)   NULL,                         -- JSON [{ "name": "...", "icon": "..." }]
        Interests          NVARCHAR(MAX)   NOT NULL CONSTRAINT DF_Profiles_Interests DEFAULT (N'[]'),
        About              NVARCHAR(2000)  NULL,

        -- Step 5: photos & KYC choice
        PhotoBlur          BIT             NOT NULL CONSTRAINT DF_Profiles_PhotoBlur DEFAULT (0),
        IdType             NVARCHAR(200)   NULL,                         -- Aadhaar Card | Passport | PAN Card | Driving License

        UpdatedAt          DATETIME2       NOT NULL CONSTRAINT DF_Profiles_UpdatedAt DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_Profiles PRIMARY KEY CLUSTERED (UserId),
        CONSTRAINT FK_Profiles_Users_UserId FOREIGN KEY (UserId)
            REFERENCES dbo.Users (Id) ON DELETE CASCADE
    );
END
GO

/* ---------------------------------------------------------------------
   6. Siblings - brothers / sisters from step 4
   --------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.Siblings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Siblings
    (
        Id             BIGINT         IDENTITY(1,1) NOT NULL,
        UserId         BIGINT         NOT NULL,
        Relation       NVARCHAR(7)    NOT NULL,                          -- brother | sister
        ClientId       NVARCHAR(200)  NULL,                              -- id generated by the app
        Name           NVARCHAR(200)  NOT NULL,
        Age            NVARCHAR(200)  NULL,
        MaritalStatus  NVARCHAR(200)  NULL,                              -- Married | Unmarried
        SortOrder      SMALLINT       NOT NULL CONSTRAINT DF_Siblings_SortOrder DEFAULT (0),

        CONSTRAINT PK_Siblings PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_Siblings_Users_UserId FOREIGN KEY (UserId)
            REFERENCES dbo.Users (Id) ON DELETE CASCADE,
        CONSTRAINT CK_Siblings_Relation CHECK (Relation IN (N'brother', N'sister'))
    );

    CREATE INDEX IX_Siblings_UserId ON dbo.Siblings (UserId);
END
GO

/* ---------------------------------------------------------------------
   7. PartnerPreferences - step 6 (one row per user)
      List columns hold JSON arrays: ["Tamil","English"]
   --------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.PartnerPreferences', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PartnerPreferences
    (
        UserId           BIGINT          NOT NULL,
        AgeMin           INT             NULL,
        AgeMax           INT             NULL,
        HeightMinCm      INT             NULL,
        HeightMaxCm      INT             NULL,
        Marital          NVARCHAR(MAX)   NOT NULL CONSTRAINT DF_PartnerPreferences_Marital    DEFAULT (N'[]'),
        Languages        NVARCHAR(MAX)   NOT NULL CONSTRAINT DF_PartnerPreferences_Languages  DEFAULT (N'[]'),
        AnyMotherTongue  BIT             NOT NULL CONSTRAINT DF_PartnerPreferences_AnyMT      DEFAULT (0),
        InterCaste       BIT             NOT NULL CONSTRAINT DF_PartnerPreferences_InterCaste DEFAULT (0),
        Religion         NVARCHAR(200)   NULL,
        Caste            NVARCHAR(200)   NULL,
        OtherCaste       NVARCHAR(200)   NULL,
        SubCaste         NVARCHAR(200)   NULL,
        OtherSubCaste    NVARCHAR(200)   NULL,
        JathagamMatch    BIT             NOT NULL CONSTRAINT DF_PartnerPreferences_Jathagam   DEFAULT (0),
        Dosham           NVARCHAR(200)   NULL,
        Education        NVARCHAR(MAX)   NOT NULL CONSTRAINT DF_PartnerPreferences_Education  DEFAULT (N'[]'),
        Employment       NVARCHAR(MAX)   NOT NULL CONSTRAINT DF_PartnerPreferences_Employment DEFAULT (N'[]'),
        Income           NVARCHAR(200)   NULL,
        Locations        NVARCHAR(MAX)   NOT NULL CONSTRAINT DF_PartnerPreferences_Locations  DEFAULT (N'[]'),
        CustomCities     NVARCHAR(MAX)   NULL,                           -- JSON [{ "name": "...", "icon": "..." }]
        OpenToRelocate   BIT             NOT NULL CONSTRAINT DF_PartnerPreferences_Relocate   DEFAULT (0),
        Expectations     NVARCHAR(2000)  NULL,
        UpdatedAt        DATETIME2       NOT NULL CONSTRAINT DF_PartnerPreferences_UpdatedAt  DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_PartnerPreferences PRIMARY KEY CLUSTERED (UserId),
        CONSTRAINT FK_PartnerPreferences_Users_UserId FOREIGN KEY (UserId)
            REFERENCES dbo.Users (Id) ON DELETE CASCADE,
        CONSTRAINT CK_PartnerPreferences_Age    CHECK (AgeMin IS NULL OR AgeMax IS NULL OR AgeMin <= AgeMax),
        CONSTRAINT CK_PartnerPreferences_Height CHECK (HeightMinCm IS NULL OR HeightMaxCm IS NULL OR HeightMinCm <= HeightMaxCm)
    );
END
GO

/* ---------------------------------------------------------------------
   8. Photos - profile photos (soft delete via DeletedAt)
   --------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.Photos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Photos
    (
        Id          BIGINT         IDENTITY(1,1) NOT NULL,
        UserId      BIGINT         NOT NULL,
        StorageKey  NVARCHAR(300)  NOT NULL,                             -- photos/{userId}/{guid}.jpg
        Url         NVARCHAR(500)  NOT NULL,                             -- public URL returned to the app
        SortOrder   SMALLINT       NOT NULL CONSTRAINT DF_Photos_SortOrder DEFAULT (0),
        IsPrimary   BIT            NOT NULL CONSTRAINT DF_Photos_IsPrimary DEFAULT (0),
        CreatedAt   DATETIME2      NOT NULL CONSTRAINT DF_Photos_CreatedAt DEFAULT (SYSUTCDATETIME()),
        DeletedAt   DATETIME2      NULL,

        CONSTRAINT PK_Photos PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_Photos_Users_UserId FOREIGN KEY (UserId)
            REFERENCES dbo.Users (Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_Photos_UserId ON dbo.Photos (UserId);
END
GO

/* ---------------------------------------------------------------------
   9. KycVerifications - blue-tick ID + selfie submissions
      Keys point to PRIVATE storage; never expose to other members.
   --------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.KycVerifications', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.KycVerifications
    (
        Id               BIGINT         IDENTITY(1,1) NOT NULL,
        UserId           BIGINT         NOT NULL,
        DocumentType     NVARCHAR(20)   NOT NULL,                        -- aadhaar | passport | pan | driving_license
        DocFrontKey      NVARCHAR(200)  NOT NULL,
        DocBackKey       NVARCHAR(200)  NULL,
        SelfieKey        NVARCHAR(200)  NOT NULL,
        VideoKey         NVARCHAR(200)  NULL,
        Status           NVARCHAR(15)   NOT NULL CONSTRAINT DF_KycVerifications_Status      DEFAULT (N'pending'),
        RejectionReason  NVARCHAR(500)  NULL,
        SubmittedAt      DATETIME2      NOT NULL CONSTRAINT DF_KycVerifications_SubmittedAt DEFAULT (SYSUTCDATETIME()),
        ReviewedAt       DATETIME2      NULL,
        ReviewedBy       NVARCHAR(200)  NULL,

        CONSTRAINT PK_KycVerifications PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_KycVerifications_Users_UserId FOREIGN KEY (UserId)
            REFERENCES dbo.Users (Id) ON DELETE CASCADE,
        CONSTRAINT CK_KycVerifications_DocumentType CHECK (DocumentType IN (N'aadhaar', N'passport', N'pan', N'driving_license')),
        CONSTRAINT CK_KycVerifications_Status       CHECK (Status IN (N'pending', N'verified', N'rejected'))
    );

    CREATE INDEX IX_KycVerifications_UserId ON dbo.KycVerifications (UserId);
END
GO

/* ---------------------------------------------------------------------
   CHECK: every row should say OK
   --------------------------------------------------------------------- */
SELECT x.TableName,
       CASE WHEN OBJECT_ID(N'dbo.' + x.TableName, N'U') IS NULL THEN N'MISSING' ELSE N'OK' END AS Status,
       (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.' + x.TableName)) AS Columns,
       x.Expected AS ExpectedColumns
FROM (VALUES (N'Users',16),(N'OtpRequests',13),(N'RefreshTokens',9),(N'OnboardingSteps',4),
             (N'Profiles',47),(N'Siblings',8),(N'PartnerPreferences',24),(N'Photos',8),(N'KycVerifications',12)
     ) AS x(TableName, Expected);
GO
