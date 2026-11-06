/* =====================================================================
   Upgrade: real SMS / email OTP delivery (Nov 2026)
   Adds 2 columns to dbo.OtpRequests. Safe to run more than once.
   SSMS: select the KalyanaVaram database in the toolbar, then F5.
   ===================================================================== */

IF COL_LENGTH(N'dbo.OtpRequests', N'VerifiedAt') IS NULL
    ALTER TABLE dbo.OtpRequests ADD VerifiedAt DATETIME2 NULL;          -- set only when the code was entered correctly
GO

IF COL_LENGTH(N'dbo.OtpRequests', N'ProviderMessageId') IS NULL
    ALTER TABLE dbo.OtpRequests ADD ProviderMessageId NVARCHAR(100) NULL; -- MSG91 / Twilio / SendGrid / SMTP message id
GO

-- Check: both rows should show a length (not NULL)
SELECT COL_LENGTH(N'dbo.OtpRequests', N'VerifiedAt')        AS VerifiedAt,
       COL_LENGTH(N'dbo.OtpRequests', N'ProviderMessageId') AS ProviderMessageId;
GO
