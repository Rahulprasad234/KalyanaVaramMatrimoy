# Real OTP delivery (SMS + Email)

## 1. What was mock and what changed

The OTP logic already met most requirements: a secure 6-digit code, an HMAC hash (never plain text), expiry, a 30 s resend cooldown, 5 sends per hour, 5 wrong attempts, single use, and rate limiting. Only **delivery** was fake.

| Before (mock) | Now |
|---|---|
| `ConsoleSmsSender` / `ConsoleEmailSender` **printed the OTP** to the console | Real providers: **Fast2SMS, MSG91, Twilio** (SMS) and **SMTP** (Gmail, Brevo, Amazon SES…) or **SendGrid** (email). The OTP is never logged |
| `Otp:DevFixedCode` (842910) used everywhere | Used **only** in Development **and** only while that channel's provider is `Console`. The API refuses to start if it's set in production |
| Code saved, then sent (a failed send still blocked a resend) | **Sent first**, saved only after the provider accepts it. A failed send leaves the previous code valid |
| No "already used" error | `OTP_ALREADY_USED` (new `VerifiedAt` column) |
| No delivery-failure error | `OTP_DELIVERY_FAILED`: 502 when the provider rejected the message, 503 when the provider is down |
| Resend only through `/otp/send` | Also `POST /v1/auth/otp/resend` |

Endpoints keep the existing naming (requirement §5: "use existing naming conventions"):

| Requirement name | This API |
|---|---|
| POST /api/auth/send-otp | `POST /v1/auth/register` (new user) or `POST /v1/auth/otp/send` (existing user) |
| POST /api/auth/verify-otp | `POST /v1/auth/otp/verify` |
| POST /api/auth/resend-otp | `POST /v1/auth/otp/resend` |

## 2. Which provider: free vs low cost (checked Nov 2026; confirm on each provider's site)

### SMS: there is no free option for real Indian SMS
Every Indian SMS provider charges per message, and **DLT registration is mandatory**. TRAI requires your business, your 6-letter sender id, and the exact OTP text to be registered; unregistered OTPs are blocked by Jio, Airtel, Vi and BSNL.

| Provider | Price per SMS (excl. 18 % GST) | Free credit | Notes |
|---|---|---|---|
| **Fast2SMS** ⭐ cheapest to start | ₹0.25 (₹100–3,999 recharge), dropping to ₹0.11 at very high volume | ₹50 on signup | Min. recharge ₹100. Set `SMS_PROVIDER=Fast2Sms` |
| **MSG91** | ~₹0.18–0.25 | — | Very popular in India; good DLT help. `SMS_PROVIDER=Msg91` |
| Twilio | Much higher for India | Trial credit | Global; easy, but expensive for India. `SMS_PROVIDER=Twilio` |
| Firebase Phone Auth | ~$0.07 per SMS (Blaze plan) | — | Works differently (client SDK); not used here |

> **Cost example:** 1,000 registrations ≈ 1,000 SMS ≈ **₹250 + GST** with Fast2SMS at the starting rate. Email OTPs can be free (below).

**Cheaper later:** WhatsApp authentication messages cost much less than SMS per message in India. The API supports a `whatsapp` channel through Twilio; this can be added later.

### Email: free options exist

| Provider | Free limit | Cost after | Use for |
|---|---|---|---|
| **Gmail SMTP** | 500 recipients/day (free Gmail), 2,000 (Workspace) | Free | **Testing** today. Needs an App Password |
| **Brevo** ⭐ | **300 emails/day, free forever**, SMTP included | Paid plans | **Production start**. Needs your domain verified |
| Resend | 3,000/month (100/day) | Paid plans | Alternative to Brevo |
| **Amazon SES** | New accounts get AWS credits, not a permanent free tier | **$0.10 per 1,000** | **Cheapest at scale** |
| SendGrid | ❌ Free plan retired in 2025 | Paid only | Supported by the code, but not recommended for low cost |

### Recommendation

| Stage | SMS | Email | Cost |
|---|---|---|---|
| Now (development) | `Console` (no SMS; dev code 842910) | **Gmail SMTP** | ₹0 |
| First real users | **Fast2SMS** (DLT) | **Brevo** free 300/day | ~₹0.25 per signup |
| Growth (1,000+ signups/day) | Fast2SMS or MSG91 volume rate | **Amazon SES** | ~₹0.11–0.18 per SMS + ~₹0.01 per email |

## 3. Account setup

### 3a. DLT registration (needed for ANY Indian SMS provider), done once
1. Register your company as a **Principal Entity** on an operator DLT portal (Jio, Airtel, Vi or BSNL). You need PAN, GST or incorporation documents and a letter of authorisation. The portal may charge a one-time fee.
2. Register a **Header (sender id)**: 6 letters, e.g. `KVMTRY`, type *Transactional/Service-Implicit*.
3. Register a **Content template** (type: Service Implicit / OTP), for example:
   ```
   {#var#} is your KalyanaVaran Matrimony verification code. Valid for 10 minutes. Do not share it with anyone.
   ```
4. Approval usually takes a few working days. Fast2SMS and MSG91 support teams can guide you.

### 3b. Fast2SMS
1. Sign up at fast2sms.com and verify your account.
2. **DLT Manager:** add your DLT entity id, the header (`KVMTRY`) and the approved template. Fast2SMS shows a **Message ID** for the template.
3. **Dev API:** copy your **API key**.
4. Configure:
   ```
   SMS_PROVIDER=Fast2Sms
   SMS_API_KEY=<API key>
   SMS_SENDER_ID=KVMTRY
   SMS_TEMPLATE_ID=<Message ID of the OTP template>
   ```
   If your template also contains the minutes (`{#var#}` twice), set `SMS_EXPIRY_VARIABLE=minutes`.

### 3c. MSG91 (alternative)
1. Sign up, then add the DLT entity, sender id and template in **SMS → Templates**, using `##otp##` as the variable.
2. Create a **Flow** with that template and copy the **template id**. Copy the **AuthKey** from your profile.
3. Configure `SMS_PROVIDER=Msg91`, `SMS_API_KEY=<AuthKey>`, `SMS_TEMPLATE_ID=<id>`, `SMS_OTP_VARIABLE=otp`.

### 3d. Gmail SMTP (free, for testing)
1. Google Account → **Security** → turn on **2-Step Verification**.
2. **App passwords** → create one named "KalyanaVaran API" → copy the 16 characters.
3. Configure `EMAIL_PROVIDER=Smtp`, `EMAIL_HOST=smtp.gmail.com`, `EMAIL_PORT=587`, `EMAIL_USERNAME=you@gmail.com`, `EMAIL_PASSWORD=<app password>`, `EMAIL_FROM=you@gmail.com`.

### 3e. Brevo SMTP (free, for production start)
1. Sign up at brevo.com and **add and verify your domain** (DNS records: SPF, DKIM, DMARC).
2. **SMTP & API → SMTP**: note the login and create an **SMTP key**. You may need to ask support to activate SMTP.
3. Configure `EMAIL_HOST=smtp-relay.brevo.com`, `EMAIL_PORT=587`, `EMAIL_USERNAME=<SMTP login>`, `EMAIL_PASSWORD=<SMTP key>`, `EMAIL_FROM=no-reply@yourdomain.com`.

### 3f. Amazon SES (later, cheapest at scale)
Verify your domain in SES (region `ap-south-1` Mumbai), request **production access** (new accounts start in a sandbox), and create **SMTP credentials**. Then set `EMAIL_HOST=email-smtp.ap-south-1.amazonaws.com` and the SMTP user and password. No code change needed.

## 4. Where to put the keys (never in the code)

**On your PC (development): user-secrets.**
```powershell
cd D:\KVM-API-2\kalyanavaran-api
dotnet user-secrets set "Sms:Provider" "Fast2Sms"
dotnet user-secrets set "Sms:ApiKey" "<key>"
dotnet user-secrets set "Sms:SenderId" "KVMTRY"
dotnet user-secrets set "Sms:TemplateId" "<message id>"
dotnet user-secrets set "Email:Provider" "Smtp"
dotnet user-secrets set "Email:Host" "smtp.gmail.com"
dotnet user-secrets set "Email:Username" "you@gmail.com"
dotnet user-secrets set "Email:Password" "<gmail app password>"
dotnet user-secrets set "Email:From" "you@gmail.com"
```
User-secrets override `appsettings.Development.json`, so the `Console` setting there no longer applies.

**On the server: environment variables.** Use the names from `.env.example` (`SMS_PROVIDER`, `SMS_API_KEY`, `EMAIL_HOST`, …). In Azure App Service, add them under **Configuration → Application settings**.

The API **checks the settings at startup**. If a key is missing it stops with a clear list, e.g. `Sms:ApiKey = Fast2SMS API key (env SMS_API_KEY)`.

To go back to the free fake mode on your PC: `dotnet user-secrets remove "Sms:Provider"` (and the same for `Email:Provider`).

## 5. Test in Scalar

1. **Check the provider:** **Dev tools → POST `/v1/dev/test-delivery`**
   ```json
   { "channel": "email", "to": "you@gmail.com" }
   { "channel": "sms",   "to": "+919876543210" }
   ```
   ✅ `"sent": true` and a `providerMessageId`, and the message arrives. ❌ The error message gives the provider's reason (wrong key, template not approved, SMTP login failed…).
2. **A1 Register** with **your real mobile and email**. You receive an SMS and an email with the **same kind of 6-digit code** (they are two different codes).
3. **A3 Verify** with `{ "otpRequestId", "mobile", "otp": "<SMS code>", "emailOtp": "<email code>" }`.
4. Try the error cases:

| Do this | Expected |
|---|---|
| Wrong code | 400 `OTP_INVALID` "… 4 attempts left" |
| Wrong code 5 times | 429 `OTP_RATE_LIMITED` "Too many wrong attempts" |
| Wait longer than the validity, then verify | 400 `OTP_EXPIRED` |
| Verify again with the same request after success | 400 `OTP_ALREADY_USED` |
| **A2b Resend** within 30 s | 429 "Please wait N seconds" |
| 6th send in one hour | 429 "Too many OTP requests" |
| Wrong `SMS_API_KEY` | 503 `OTP_DELIVERY_FAILED` "SMS service is temporarily unavailable" |
| Email rejected (bad address) | 502 `OTP_DELIVERY_FAILED` "couldn't send the OTP to your email" |
| Invalid mobile / email format | 400 `VALIDATION_ERROR` |

Check the database:
```sql
SELECT TOP 10 Id, Channel, Destination, Attempts, CreatedAt, ExpiresAt, ConsumedAt, VerifiedAt, ProviderMessageId
FROM dbo.OtpRequests ORDER BY CreatedAt DESC;
```
`OtpHash` is a hash; the real code is never stored or logged.

## 6. Error codes added

| Code | HTTP | When |
|---|---|---|
| `OTP_DELIVERY_FAILED` | 502 | Provider rejected the message (bad number or address, template mismatch) |
| `OTP_DELIVERY_FAILED` | 503 | Provider down, unreachable, or wrong credentials |
| `OTP_ALREADY_USED` | 400 | The same OTP request was already verified |

At registration, the **SMS must succeed**; the email OTP is best-effort. If email fails, registration still succeeds and the user can resend the email code with `/otp/resend` `{ "channel": "email" }`.

## 7. Security checklist
- CSPRNG 6-digit code, HMAC-SHA256 hash with `Otp:HashSecret`, constant-time compare
- Expiry, 30 s cooldown, 5 sends/hour per channel, 5 wrong attempts, single use (`VerifiedAt`)
- IP rate limit 10/min on `/v1/auth/*` (middleware)
- OTP never logged; phone and email masked in logs (`+91******3210`, `yo***@gmail.com`)
- Secrets only in user-secrets or env vars; startup blocks console providers and the fixed dev code outside Development
- Use HTTPS in production (HSTS + redirect are already on outside Development)

## 8. Files

**New:**
- `Services/Delivery/DeliveryCore.cs`: options, interfaces, `OtpDeliveryException`, masking
- `Services/Delivery/Fast2SmsSender.cs`, `Msg91SmsSender.cs`, `TwilioSmsSender.cs`
- `Services/Delivery/SmtpEmailSender.cs` (MailKit), `SendGridEmailSender.cs`, `OtpEmailTemplate.cs`
- `Services/Delivery/ConsoleSenders.cs`: dev only, no OTP in logs
- `Services/Delivery/OtpDeliveryRegistration.cs`: provider selection, env var names, startup validation
- `Database/Upgrade_2026-11_RealOtpDelivery.sql`
- `.env.example`, `docs/OTP_DELIVERY.md`

**Modified:**
- `Services/OtpService.cs`: send-then-save, delivery errors, dev code rules, `VerifiedAt`, already-used
- `Services/AuthService.cs`: email failure at registration no longer blocks registration
- `Controllers/AuthController.cs`: `POST /v1/auth/otp/resend`
- `Controllers/DevController.cs`: `POST /v1/dev/test-delivery`
- `Entities/Entities.cs`, `Data/AppDbContext.cs`: `VerifiedAt`, `ProviderMessageId`
- `Common/ApiResponse.cs`: new error codes
- `Program.cs`: `AddOtpDelivery`, env aliases
- `appsettings*.json`, `KalyanaVaran.Api.csproj` (+ MailKit)
- `Database/KalyanaVaram_AzureSQL.sql`, `KalyanaVaran_CreateTables.sql`, `AuthFlow_Queries.sql`
