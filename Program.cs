using System.Text;
using KalyanaVaran.Api.Common;
using KalyanaVaran.Api.Data;
using KalyanaVaran.Api.Middleware;
using KalyanaVaran.Api.OpenApi;
using KalyanaVaran.Api.Services;
using KalyanaVaran.Api.Services.Delivery;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// SMS_PROVIDER, SMS_API_KEY, EMAIL_HOST, EMAIL_PASSWORD … env vars (see docs/OTP_DELIVERY.md)
config.AddOtpDeliveryEnvironmentAliases();

builder.WebHost.ConfigureKestrel(k => k.AddServerHeader = false);   // hide "Server: Kestrel"

// ---------- Options ----------
builder.Services.Configure<JwtOptions>(config.GetSection("Jwt"));
builder.Services.Configure<OtpOptions>(config.GetSection("Otp"));
builder.Services.Configure<StorageOptions>(config.GetSection("Storage"));

var jwt = config.GetSection("Jwt").Get<JwtOptions>() ?? throw new InvalidOperationException("Jwt section missing");
if (Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32)
    throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters.");

// ---------- Database ----------
// Entity Framework Core → SQL Server / Azure SQL (connection string: user-secrets or env var ConnectionStrings__Default)
var connectionString = config.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Contains("SET_IN_USER_SECRETS"))
    throw new InvalidOperationException(
        "Database password not set. Run: dotnet user-secrets set \"ConnectionStrings:Default\" \"<your connection string>\"");

builder.Services.AddDbContext<AppDbContext>(o =>
{
    o.UseSqlServer(connectionString, sql =>
    {
        // Azure SQL drops connections briefly from time to time; retry instead of failing the request
        sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
        sql.CommandTimeout(30);
    });
    if (builder.Environment.IsDevelopment()) o.EnableDetailedErrors();
});

// ---------- MVC + JSON + validation envelope ----------
builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter());
        o.JsonSerializerOptions.Converters.Add(new LenientStringConverter());   // "otp": 842910 → "842910"
    })
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ValidationResponse.Create);

// ---------- Auth ----------
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.Sub
        };
    });
builder.Services.AddAuthorization();

// ---------- CORS ----------
var origins = config.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(origins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders(RequestIdMiddleware.Header)));

// ---------- OpenAPI (Scalar reads /openapi/v1.json) ----------
builder.Services.AddOpenApi(o => o.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

// ---------- Rate limiting + cache (used by middleware) ----------
builder.Services.AddApiRateLimiting(config);
builder.Services.AddMemoryCache();

// ---------- App services ----------
// Real OTP delivery: Sms:Provider = Fast2Sms | Msg91 | Twilio | Console(dev), Email:Provider = Smtp | SendGrid | Console(dev)
builder.Services.AddOtpDelivery(config, builder.Environment);
builder.Services.AddScoped<OtpService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<CompletionService>();
builder.Services.AddScoped<OnboardingService>();
builder.Services.AddScoped<ProfileService>();
builder.Services.AddScoped<MediaService>();

var app = builder.Build();

// Dev: create the database automatically (switch to EF migrations before production)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // With migrations: apply new ones. Without: create tables only if the database is empty
    // (your Azure DB already has them from KalyanaVaram_AzureSQL.sql, so nothing happens).
    if (db.Database.GetMigrations().Any()) await db.Database.MigrateAsync();
    else await db.Database.EnsureCreatedAsync();
}

// ================= Middleware pipeline (ORDER MATTERS) =================
// A request runs top → bottom, and the response comes back bottom → top.

app.UseMiddleware<RequestIdMiddleware>();        // 1. X-Request-Id for every log line + meta.requestId
app.UseMiddleware<RequestLoggingMiddleware>();   // 2. one log line per request (method, path, status, ms, user)
app.UseMiddleware<ExceptionMiddleware>();        // 3. any exception → error envelope (logged by #2 with final status)
app.UseMiddleware<SecurityHeadersMiddleware>();  // 4. nosniff, no iframes, no-store on /v1

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();                               // 5. force HTTPS in browsers (production only)
    app.UseHttpsRedirection();
}

// 6. Empty-body errors (401 from JWT, unknown route 404, 405...) → standard envelope
app.UseStatusCodePages(async ctx =>
{
    var http = ctx.HttpContext;
    var (code, message) = ApiEnvelopeWriter.ForStatus(http.Response.StatusCode);
    await ApiEnvelopeWriter.WriteErrorAsync(http, http.Response.StatusCode, code, message);
});

app.UseStaticFiles();                            // 7. serves wwwroot/uploads/photos/... (short-circuits)
app.UseRouting();                                // 8. pick the endpoint (needed for per-endpoint rate limits)
app.UseCors();                                   // 9. browser origins from Cors:Origins
app.UseAuthentication();                         // 10. read the JWT → HttpContext.User
app.UseMiddleware<ActiveUserMiddleware>();       // 11. token valid but user blocked/deleted → 403/401
app.UseRateLimiter();                            // 12. global 300/min/IP, auth 10/min/IP, uploads 20/min/user
app.UseAuthorization();                          // 13. [Authorize] checks

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();                                   // /openapi/v1.json
    app.MapScalarApiReference(o => o.Title = "KalyanaVaran API");  // /scalar
    app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();
}

app.MapControllers();

app.Run();
