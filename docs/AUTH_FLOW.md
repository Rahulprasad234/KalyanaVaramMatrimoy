# Get OTP → Verify → Token: flow, responses and analysis

Sources compared:
- `API_REQUIREMENTS.docx` (Draft v1, 23 Oct 2026)
- The live UI at https://divine-matrimony-web.web.app: the `/login/` and `/otp/` screens and their JS bundle, checked on 27 Oct 2026
- This API

Base URL for the frontend: `NEXT_PUBLIC_API_BASE_URL=http://localhost:5080/v1`. The UI appends paths like `/auth/register` to it, so the value must end in `/v1`.

---

## 1. Flow

```
/login  "Get OTP & Start Profile"
   │  POST /auth/register ───────────► users (pending) + otp_requests (sms) + otp_requests (email)
   │  ◄── { otpRequestId, mobile, channel, expiresInSeconds: 600, resendAfterSeconds: 30 }
   │  UI saves it to localStorage "kv_auth_pending_otp" and goes to /otp/
   ▼
/otp   "Resend OTP" (mobile)  → POST /auth/otp/send { mobile, channel: "sms" }
       "Resend OTP" (email)   → POST /auth/otp/send { mobile, channel: "email" }    ← UI must wire this
       "Continue"             → POST /auth/otp/verify { otpRequestId, mobile, otp, emailOtp }
   │  ◄── { tokens: { accessToken, refreshToken, tokenType, expiresIn }, user: {...} }
   │  UI stores accessToken in "dm-access-token" and refreshToken in "kv_auth_refresh_token"
   ▼
/onboarding/{user.onboardingStep}  (7 = finished → home)
```

Dev OTP for both codes: **842910** (`Otp:DevFixedCode`, Development only). This is the same code the UI mock accepts.

---

## 2. Requests and responses

All responses use the envelope `{ success, data, error, meta: { requestId, timestamp } }`. The UI shows `error.message` to the user as-is.

### A1 `POST /v1/auth/register`

The UI builds this body from the form. It lowercases gender, strips the mobile to digits and adds `+91`, and sends the terms checkbox as `acceptedTerms`.

```json
{ "profileFor": "myself", "fullName": "Radhika Sharma", "gender": "female",
  "mobile": "+919876543210", "email": "family@example.com", "acceptedTerms": true }
```

200:
```json
{ "success": true,
  "data": { "otpRequestId": "otp_7f3a9c21ab12", "mobile": "+919876543210", "channel": "sms",
            "expiresInSeconds": 600, "resendAfterSeconds": 30 },
  "error": null,
  "meta": { "requestId": "6f1c…", "timestamp": "2026-10-27T07:35:00.000Z" } }
```
An email OTP is sent at the same time, to match the screen text "Sent via SMS & email".

| Case | HTTP | `error.code` | `error.message` (shown on /login) |
|---|---|---|---|
| Bad mobile | 400 | VALIDATION_ERROR | Please enter a valid 10-digit Indian mobile number. |
| Bad email | 400 | VALIDATION_ERROR | Please enter a valid email address. |
| Terms not accepted | 400 | VALIDATION_ERROR | Please accept the Terms of Use and Privacy Policy to continue. |
| Mobile already verified | 409 | CONFLICT | This mobile number is already registered. Please log in. |
| Email already verified | 409 | CONFLICT | This email is already registered. Please log in. |
| Within 30 s of last OTP | 429 | OTP_RATE_LIMITED | Please wait 18 seconds before requesting a new OTP. |
| More than 5 OTPs in an hour | 429 | OTP_RATE_LIMITED | Too many OTP requests. Please try again after some time. |

Validation errors also carry `error.details`, e.g. `{ "mobile": "Please enter a valid…" }`.

### A2 `POST /v1/auth/otp/send` (resend, or existing-member login)
```json
{ "mobile": "+919876543210", "channel": "sms" }      // sms | whatsapp | email
```
200: same `data` as A1. Errors: 404 `NOT_FOUND` "No account found for this mobile number. Please register first.", 403 `FORBIDDEN` (blocked), 429 as above. Phone and email codes have **separate** 30-second and 5-per-hour limits.

### A3 `POST /v1/auth/otp/verify`
```json
{ "otpRequestId": "otp_7f3a9c21ab12", "mobile": "+919876543210", "otp": "842910", "emailOtp": "842910" }
```
`emailOtp` is optional. If it's sent and correct, `emailVerified` becomes true. The API always checks the **newest** unused code for the mobile, so an older `otpRequestId` still works after a resend.

200:
```json
{ "success": true,
  "data": {
    "tokens": { "accessToken": "eyJhbGciOi…", "refreshToken": "rt_Q2x…", "tokenType": "Bearer", "expiresIn": 3600 },
    "user": { "memberId": "DM-984210", "fullName": "Radhika Sharma", "mobile": "+919876543210",
              "email": "family@example.com", "gender": "female", "profileFor": "myself",
              "mobileVerified": true, "emailVerified": true, "isNewUser": true, "onboardingStep": 1 } },
  "error": null, "meta": { "requestId": "…", "timestamp": "…" } }
```

| Case | HTTP | `error.code` | `error.message` (shown on /otp) | `details` key |
|---|---|---|---|---|
| Wrong mobile code | 400 | OTP_INVALID | The mobile OTP you entered is incorrect. 4 attempts left. | `otp` |
| Wrong email code | 400 | OTP_INVALID | The email OTP you entered is incorrect. 4 attempts left. | `emailOtp` |
| Code older than 10 min | 400 | OTP_EXPIRED | This OTP has expired. Please request a new one. | `otp` |
| 5 wrong attempts | 429 | OTP_RATE_LIMITED | Too many wrong attempts. Please request a new OTP. | `otp` |
| No code was ever sent | 400 | OTP_INVALID | This OTP request is not valid. Please request a new OTP. | – |

The access token is a JWT, HS256, valid for 1 hour: `sub` = internal user id, `member_id` = DM-xxxxxx. The refresh token is an opaque string, valid for 30 days, and rotates on every `/auth/refresh`.

---

## 3. Requirements doc vs UI vs API

| # | Topic | Requirements doc | Live UI | API now |
|---|---|---|---|---|
| 1 | **Email OTP** | §8.3: no endpoint, suggests a new `email-otp/send` + verify | Shows a second "Email OTP" box and "Enter both 6-digit codes", but **never sends the email code**; the email "Resend" only restarts the timer | Email code sent on register. Resend with `channel:"email"`. Optional `emailOtp` in verify, so no new endpoints are needed. **Needs sign-off** |
| 2 | Resend → otpRequestId | Not covered | After resend, the page **keeps the old `otpRequestId`**, because it reads localStorage only on page load | Verify uses the newest code for the mobile, so the old id is harmless |
| 3 | Resend cooldown | 30 s | Timer starts at **0**, so the user can resend immediately; resend errors are **ignored** | Returns 429 "Please wait N seconds…" |
| 4 | Mobile OTP box | – | **Pre-filled with `842910`**, a mock leftover | Dev code set to 842910 so testing still works |
| 5 | `user.onboardingStep` | Next step 1–6, 7 = done | "Continue" always goes to `/onboarding/1/` | Returned correctly; the UI should use it |
| 6 | Existing-member login | A2 is for existing members | **No login screen**: "Sign In" / "Log In" go to `/matches/`; re-registering gets a 409 | A2 + A3 work; the UI needs a "mobile only → OTP" screen |
| 7 | Unknown mobile on A2 | TO CONFIRM: 404 or silent | – | 404, so the UI can say "please register". This does let someone check whether a number is registered |
| 8 | 401 → refresh | §2.3: client calls `/auth/refresh` on 401 | The client has **no refresh logic**; it just returns the error | `/auth/refresh` is ready, with rotation |
| 9 | Gender | `male` \| `female` | Radio values `Male`/`Female`, lowercased before sending | Expects lowercase. Matches |
| 10 | Mobile format | E.164 `+91` + 10 digits | Strips non-digits; 10 digits → `+91…`, anything else → `+digits` | Accepts `+91[6-9]XXXXXXXXX` only, with a friendly message |
| 11 | Headers | `X-Request-Id`, `X-App-Version`, `X-Platform`, `Accept-Language` | Sends all of them | Echoes `X-Request-Id`, logs platform and version; CORS allows all headers |
| 12 | Full name split | TO CONFIRM (§8.13) | Mock splits on the first space | Server splits into `Profiles.FirstName` / `LastName` |
| 13 | Google sign-up | A4, TO CONFIRM | "Sign up with Google" button, not wired | Not built yet |
| 14 | Steps | Wizard has 6 steps | /login says "Step 1 of 4" | Wording mismatch in the UI |
| 15 | Token lifetime | `expiresIn` 3600, TO CONFIRM | Stores token, ignores `expiresIn` | Access 1 h, refresh 30 d (`Jwt` config) |
| 16 | Error messages | `error.message` shown as-is | Shows `error.message` on both screens | All auth messages are written for end users |

### Frontend changes needed (for the web team)
1. `/otp`: start the mobile OTP state as `""`, not `"842910"`.
2. `/otp`: send `emailOtp` in the verify body, and make the email "Resend OTP" call `POST /auth/otp/send {mobile, channel:"email"}`.
3. `/otp`: start both resend timers at `resendAfterSeconds` (30) when the page opens, and show resend errors (429).
4. `/otp`: after a resend, update the in-page `otpRequestId` from the response. The API tolerates this today, but the code is cleaner with it.
5. `/otp`: after verify, go to `/onboarding/{user.onboardingStep}/`, or home when it is 7.
6. Add a login screen for existing members (mobile number → A2 → `/otp`). Point "Sign In" and "Log In" to it instead of `/matches/`.
7. API client: on `UNAUTHORIZED`, call `/auth/refresh` once, save the new tokens, then retry the request.
8. Set `NEXT_PUBLIC_USE_MOCK_API=false` and `NEXT_PUBLIC_API_BASE_URL=https://<api-host>/v1`. The deployed `https://…web.app` site can only call an **https** API; browsers block http.

---

## 4. Tables touched

| Step | Tables |
|---|---|
| Register | `Users` (insert/update, pending) → `OtpRequests` ×2 (sms + email) |
| Resend | `OtpRequests` (retire older codes of the same kind, insert new) |
| Verify | `OtpRequests` (attempts / consumed) → `Users` (active, verified flags) → `Profiles` (first login) → `RefreshTokens` |
| Refresh | `RefreshTokens` (revoke old, insert new, link) |
| Logout | `RefreshTokens` (revoke all) |

The exact T-SQL for each step is in `Database/AuthFlow_Queries.sql`.
