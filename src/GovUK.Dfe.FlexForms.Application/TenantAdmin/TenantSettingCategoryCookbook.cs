using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Tenancy;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin;

/// <summary>
/// Static cookbook of known TenantConfig categories for SuperAdmin UI guidance.
/// </summary>
public static class TenantSettingCategoryCookbook
{
    public static IReadOnlyList<TenantSettingCategoryCookbookEntryDto> All { get; } =
    [
        Entry(
            "Authentication",
            "Explicit interactive login scheme for this tenant.",
            ["Web", "Shared"],
            example: """{"Scheme":"DfESignIn"}""",
            notes: ["Values: TestAuthentication, EntraSso, DfESignIn", "Wins over provider Enabled flags"],
            requiresObject: true),

        Entry(
            "TestAuthentication",
            "Local/dev JWT login without an external IdP.",
            ["Web", "Shared"],
            example: """{"Enabled":false,"JwtSigningKey":"...","JwtIssuer":"...","JwtAudience":"..."}""",
            notes: ["Forced secret category", "When Enabled=true, signing fields are required"],
            requiresObject: true),

        Entry(
            "EntraSso",
            "Microsoft Entra ID (Azure AD) interactive SSO.",
            ["Web", "Shared"],
            example: """{"Enabled":false,"TenantId":"...","ClientId":"...","ClientSecret":"..."}""",
            notes: ["Forced secret category", "Enabled may be a boolean or string"],
            requiresObject: true),

        Entry(
            "DfESignIn",
            "DfE Sign-in (OpenID Connect) interactive login.",
            ["Web", "Shared"],
            example: """{"Authority":"https://...","ClientId":"...","ClientSecret":"...","CallbackPath":"/signin-oidc"}""",
            notes: ["Forced secret category"],
            requiresObject: true),

        Entry(
            "Authorization",
            "API token settings used to mint/validate exchanged user JWTs.",
            ["Api", "Shared"],
            example: """{"TokenSettings":{"SecretKey":"...","Issuer":"...","Audience":"..."}}""",
            notes: ["Forced secret category", "Flat SecretKey/Issuer/Audience also accepted"],
            requiresObject: true),

        Entry(
            "ConnectionStrings",
            "Database connection strings for the tenant.",
            ["Api", "Shared"],
            example: """{"DefaultConnection":"Server=...;Database=...;"}""",
            notes: ["Forced secret category", "Named connections (not only DefaultConnection) are allowed"],
            requiresObject: true),

        Entry(
            "InternalServiceAuth",
            "Service-to-service JWT credentials and optional API keys.",
            ["Api", "Web", "Shared"],
            example: """{"SecretKey":"...","Issuer":"...","Audience":"...","ServiceApiKeys":[]}""",
            notes: ["Forced secret category"],
            requiresObject: true),

        Entry(
            "AllowedHosts",
            "ASP.NET AllowedHosts for the API/Web host.",
            ["Api", "Web"],
            example: """["localhost"]""",
            notes: ["May be a JSON array or string"],
            requiresObject: false),

        Entry(
            "FeatureManagement",
            "Feature flags for the tenant.",
            ["Api", "Web", "Shared"],
            example: """{"MyFeature":true}""",
            notes: ["May be an object, or a bare boolean for simple flags"],
            requiresObject: false),

        Entry(
            "Layout",
            "UI branding / layout options for the Web app.",
            ["Web"],
            example: """{"ServiceName":"My service","Phase":"beta"}""",
            notes: ["Unknown object categories only need valid JSON"],
            requiresObject: false),

        Entry(
            "AuthProviders",
            "Machine/service auth providers (API keys, mTLS, client credentials).",
            ["Api", "Shared"],
            example: """{"Providers":[]}""",
            notes: ["Forced secret category"],
            requiresObject: false),
    ];

    private static TenantSettingCategoryCookbookEntryDto Entry(
        string category,
        string description,
        string[] targets,
        string example,
        string[] notes,
        bool requiresObject)
        => new(
            category,
            description,
            targets,
            TenantSettingsSecretCategories.ShouldEncrypt(category),
            requiresObject,
            example,
            notes);
}
