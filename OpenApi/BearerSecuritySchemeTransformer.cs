using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace KalyanaVaran.Api.OpenApi;

/// <summary>Adds the "Bearer" JWT scheme so Scalar shows an auth box where you paste the accessToken.</summary>
internal sealed class BearerSecuritySchemeTransformer(IAuthenticationSchemeProvider schemes) : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var all = await schemes.GetAllSchemesAsync();
        if (!all.Any(s => s.Name == "Bearer")) return;

        document.Info.Title = "KalyanaVaran Matrimony API";
        document.Info.Version = "v1";
        document.Info.Description =
            "Auth + profile completion. Flow: A1 register → A3 verify (dev OTP 842910) → " +
            "copy tokens.accessToken into Authentication → onboarding steps 1–6 → photos / KYC.";

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Paste tokens.accessToken from POST /v1/auth/otp/verify"
        };

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = []
        });
    }
}
