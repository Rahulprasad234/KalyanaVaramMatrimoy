# KalyanaVaran API (.NET 10)

Auth (OTP + JWT) and the 6-step profile wizard, built to match `API_REQUIREMENTS.docx`.

**Stack:** ASP.NET Core 10 (controllers) · EF Core 10 + SQL Server · JWT bearer · Scalar UI

## 1. Run it

**1) Database.** Pick one option and put its connection string in `appsettings.json` → `ConnectionStrings:Default`.

| You have | Connection string |
|---|---|
| SQL Server in Docker (`docker compose up -d`) | `Server=localhost,1433;Database=KalyanaVaran;User Id=sa;Password=KalyanaVaran@123;TrustServerCertificate=True;MultipleActiveResultSets=True` *(default)* |
| SQL Server Express (Windows) | `Server=.\\SQLEXPRESS;Database=KalyanaVaran;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True` |
| LocalDB (installed with Visual Studio) | `Server=(localdb)\\MSSQLLocalDB;Database=KalyanaVaran;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True` |
| Full SQL Server, Windows login | `Server=localhost;Database=KalyanaVaran;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True` |

Backslashes are doubled (`\\`) because the value goes inside JSON.

You don't need to create the database. In Development the API creates the `KalyanaVaran` database and all its tables on first run.

**2) Run the API**
```bash
dotnet run --launch-profile http
```

Then open **http://localhost:5080/scalar**.

In Development every OTP (mobile and email) is **842910**, the same code the web UI's mock uses. Codes are also printed in the console as `[DEV SMS] ...` and `[DEV EMAIL] ...`. See `docs/AUTH_FLOW.md` for the full login/OTP flow.

## 2. Test the flow in Scalar

1. **Auth → A1 Register.** Send:
   ```json
   { "profileFor": "myself", "fullName": "Radhika Sharma", "gender": "female",
     "mobile": "+919876543210", "email": "family@example.com", "acceptedTerms": true }
   ```
   Copy `data.otpRequestId` from the response.
2. **Auth → A3 Verify OTP.** Send:
   ```json
   { "otpRequestId": "<from step 1>", "mobile": "+919876543210", "otp": "842910", "emailOtp": "842910" }
   ```
   Copy `data.tokens.accessToken` from the response.
3. Click **Authentication** (top of Scalar) → **Bearer**, and paste the token.
4. **Onboarding → Step 1 … Step 6.** Each response shows `completion.percent` and `nextStep`.
5. **Onboarding → P3 GET step**, e.g. `/v1/profile/me/onboarding/2`, returns exactly what you saved.
6. **Photos & KYC → P6 upload photo**, then pass the returned URL in **Step 5** → `photos`.
7. **P8 Submit KYC** (multipart: `documentType` + files) → **P9** shows `pending`.
   To test approval, use **Dev tools → POST `/v1/dev/kyc/{memberId}/review`** with `{ "status": "verified" }`.
8. **P1 GET /v1/profile/me** returns the full profile.

Sample bodies for each step:

```jsonc
// Step 1
{ "dobDay": "14", "dobMonth": "August", "dobYear": "1997", "marital": "Never Married",
  "height": "5'5\" (165 cm)", "weightKg": "62", "bodyType": "Athletic", "complexion": "Wheatish", "disability": "No" }
// Step 2
{ "religion": "Hindu", "caste": "Brahmin - Iyer", "subCaste": "Vadama", "gothram": "Kasyapa",
  "nakshatra": "Rohini", "rasi": "Rishabham (Vrishabha / Taurus)", "dosham": "No Dosham",
  "birthTime": "06:45 AM", "birthPlace": "Chennai", "autoGenerateChart": true }
// Step 3
{ "education": "MBA / PGDM", "college": "IIM Bangalore", "employmentSector": "Private Sector",
  "profession": "Product Manager", "employer": "Infosys", "annualIncome": "₹18 Lakhs - ₹25 Lakhs per annum",
  "incomeBand": "₹18 - 25L", "workLocation": "Bengaluru", "openToRelocate": true }
// Step 4
{ "familyType": "Nuclear Family", "familyValues": "Moderate", "fatherOccupation": "Retired",
  "motherOccupation": "Teacher",
  "brotherList": [ { "id": "b1", "name": "Arjun", "age": "30", "maritalStatus": "Married" } ],
  "sisterList": [], "diet": "Vegetarian", "smoking": "Never", "drinking": "Never",
  "hobbies": ["Music", "Travel"], "customHobbies": [ { "name": "Kolam", "icon": "palette" } ],
  "about": "Warm, family-oriented..." }
// Step 5
{ "photos": ["http://localhost:5080/uploads/photos/1/....jpg"], "blur": false, "idType": "Aadhaar Card" }
// Step 6
{ "partnerAgeMin": 27, "partnerAgeMax": 33, "partnerHeightMin": 170, "partnerHeightMax": 185,
  "partnerMarital": ["Never Married"], "partnerLanguages": ["Tamil", "English"], "anyMotherTongue": false,
  "interCaste": false, "partnerReligion": "Hindu (or Any)", "jathagamMatch": true,
  "partnerDosham": "Doesn't Matter", "partnerEducation": ["B.Tech / B.E.", "MBA / PGDM"],
  "partnerEmployment": ["Private Sector"], "partnerIncome": "₹10L+",
  "partnerLocations": ["Chennai", "Bengaluru"], "partnerOpenToRelocate": true }
```

## 3. Endpoints

| # | Method | Path | Auth |
|---|---|---|---|
| A1 | POST | `/v1/auth/register` | – |
| A2 | POST | `/v1/auth/otp/send` | – |
| A3 | POST | `/v1/auth/otp/verify` | – |
| A5 | POST | `/v1/auth/refresh` | – |
| A6 | POST | `/v1/auth/logout` | Bearer |
| P1 | GET | `/v1/profile/me` | Bearer |
| P2 | PUT | `/v1/profile/me` | Bearer |
| P3 | GET | `/v1/profile/me/onboarding/{step}` | Bearer |
| P4 | PUT | `/v1/profile/me/onboarding/{1..6}` | Bearer |
| P5 | GET | `/v1/profile/me/completion` | Bearer |
| P6 | POST | `/v1/profile/me/photos` (multipart `file`) | Bearer |
| P7 | DELETE | `/v1/profile/me/photos` `{ url }` | Bearer |
| P8 | POST | `/v1/profile/me/verification` (multipart) | Bearer |
| P9 | GET | `/v1/profile/me/verification` | Bearer |

Every response uses the envelope `{ success, data, error, meta: { requestId, timestamp } }`. This includes validation errors, 401, unknown routes and 500s. `X-Request-Id` is echoed back.

## 4. Middleware pipeline (order in Program.cs)

| # | Middleware | What it does |
|---|---|---|
| 1 | `RequestIdMiddleware` | Reads `X-Request-Id` from the client or creates one. It is echoed back, used as `meta.requestId`, and attached to every log line. |
| 2 | `RequestLoggingMiddleware` | Writes one line per request with method, path, status, time in ms, user ID, platform and app version. It never logs bodies or query strings. |
| 3 | `ExceptionMiddleware` | Turns any exception into the error envelope (validation, 409 duplicate key, 413, 500). |
| 4 | `SecurityHeadersMiddleware` | Adds `nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`, and `Cache-Control: no-store` on `/v1`. |
| 5 | HSTS + HTTPS redirect | Production only. |
| 6 | Status code pages | Gives empty 401/404/405 responses a JSON envelope body. |
| 7–10 | Static files → Routing → CORS → Authentication | Built-in ASP.NET Core middleware. |
| 11 | `ActiveUserMiddleware` | Blocked or deleted users get 403/401 even if their token is still valid. Status is cached for 60 s. |
| 12 | Rate limiter | 300 requests/min per IP overall. 10/min per IP on `/v1/auth/*`. 20/min per user on photo and KYC uploads. Configure under `RateLimits` in appsettings. |
| 13 | Authorization | Enforces `[Authorize]`. |

To test rate limiting in Scalar, call **A2 Send OTP** 11 times within a minute. The 11th call returns 429 with a `Retry-After` header.

## 5. Project layout

```
Program.cs                 DI, JWT, CORS, OpenAPI + Scalar, middleware
Common/                    Envelope, ApiException, error writer, options, label parsing
Middleware/                Request ID, logging, exceptions, security headers, active-user check, rate limits
Entities/Entities.cs       DB tables (Users, OtpRequests, RefreshTokens, OnboardingSteps,
                           Profiles, Siblings, PartnerPreferences, Photos, KycVerifications)
Data/AppDbContext.cs       EF Core mapping for SQL Server (lists stored as JSON in nvarchar(max))
Contracts/                 Request/response DTOs with validation
Services/                  Auth, OTP, tokens, onboarding, profile, completion, media
Controllers/               Auth, Profile (incl. onboarding/photos/KYC), Dev
```

## 6. Before production

- **Secrets:** move `Jwt:SigningKey`, `Otp:HashSecret` and the DB password into user-secrets or environment variables.
- **Dev OTP:** remove `Otp:DevFixedCode`. It is only set in `appsettings.Development.json`.
- **Migrations:** switch from `EnsureCreated` to EF migrations:
  ```bash
  dotnet tool install -g dotnet-ef
  dotnet ef migrations add Initial
  dotnet ef database update
  ```
  Startup runs migrations automatically once any exist.
- **SMS:** replace `ConsoleSmsSender` with a real provider (MSG91, Twilio, Gupshup) by implementing `ISmsSender`.
- **Storage:** move photos to S3/GCS/Azure Blob behind a CDN, and KYC files to a private bucket with encryption at rest.
- **Behind a load balancer or reverse proxy:** add `app.UseForwardedHeaders()` so rate limits and logs see the real client IP.
- **Not built yet:** Google sign-in (A4) and email OTP. Both are still "TO CONFIRM" in the requirements doc.
