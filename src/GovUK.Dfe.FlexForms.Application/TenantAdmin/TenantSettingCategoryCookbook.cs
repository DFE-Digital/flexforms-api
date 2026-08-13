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
            notes: ["Values: TestAuthentication, EntraSso, DfESignIn", "Wins over provider Enabled flags", "TestAuthentication is ignored in Production"],
            requiresObject: true),

        Entry(
            "TestAuthentication",
            "Local/dev JWT login without an external IdP.",
            ["Web", "Shared"],
            example: """{"Enabled":false,"JwtSigningKey":"...","JwtIssuer":"...","JwtAudience":"..."}""",
            notes: ["Forced secret category", "When Enabled=true, signing fields are required", "Cannot be enabled in Production"],
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
            example: """{"Providers":[{"Name":"file-validation","Kind":"ApiKey","IsServicePrincipal":true,"KeyHash":"<sha256-hex>","Roles":["FileValidation"]}]}""",
            notes:
            [
                "Forced secret category",
                "Additive: do not copy TokenSettings/Entra/DSI here. API-key rows only."
            ],
            requiresObject: false),

        Entry(
            "ApplicationTerminology",
            "Display terms for 'application' (delegated to Tenant Admins).",
            ["Web"],
            example: """{"Singular":"application","Plural":"applications"}""",
            notes: ["Non-secret", "Also editable via Organisation Settings"],
            requiresObject: true),

        Entry(
            "NotificationBanner",
            "Site-wide GOV.UK notification banner (delegated to Tenant Admins).",
            ["Web"],
            example: """{"Enabled":false,"Heading":"Important","Message":""}""",
            notes: ["Non-secret", "Also editable via Organisation Settings"],
            requiresObject: true),

        Entry(
            "Dashboard",
            "Application listing page size and filters (delegated to Tenant Admins).",
            ["Web"],
            example: """{"PageSize":50,"EnableApplicationFilters":false}""",
            notes: ["Non-secret", "Also editable via Organisation Settings"],
            requiresObject: true),

        Entry(
            "EventMappings",
            "Per-template field mappings for typed and schema events (delegated to Tenant Admins).",
            ["Shared"],
            example: """{"form-001":{"TransferApplicationSubmittedEvent":{"mappingId":"...","eventType":"TransferApplicationSubmittedEvent","fieldMappings":{}}}}""",
            notes:
            [
                "Non-secret",
                "Saved with Target=Shared so the API runtime can read it",
                "Also editable via Event mappings Admin page"
            ],
            requiresObject: true),

        Entry(
            "SchemaEvents",
            "Tenant-defined schema events (topic + JSON Schema) for messages not yet in CoreLibs.",
            ["Shared"],
            example: """{"MyCustomSubmitted":{"topicName":"my-custom-submitted","version":"1.0","description":"...","jsonSchema":{"type":"object","properties":{}}}}""",
            notes:
            [
                "Non-secret",
                "Saved with Target=Shared so the API runtime can read it",
                "Use EventKind=Schema in EventTriggers",
                "Promote successful schemas into CoreLibs when stable"
            ],
            requiresObject: true),

        Entry(
            "FileValidation",
            "Per-template policy for blocking submit when a tenant function reports a file as invalid.",
            ["Shared"],
            example: """{"DefaultMode":"Off","Extensions":[".xlsx"],"Templates":{"00000000-0000-0000-0000-000000000000":"RequirePassed"}}""",
            notes:
            [
                "Non-secret",
                "Saved with Target=Shared so the API runtime can read it",
                "Modes: Off (ignore), FailOnInvalid (block Failed only), RequirePassed (Pending also blocks)",
                "Optional Extensions: only matching uploads are marked Pending (omit or [] = all files)",
                "Tenant Azure Functions report results via POST /v1/integrations/files/{fileId}/validation-result"
            ],
            requiresObject: true),

        Entry(
            "EventTriggers",
            "Events the API publishes at each application lifecycle trigger (delegated to Tenant Admins).",
            ["Shared"],
            example: """{"ApplicationSubmitted":[{"eventKind":"Typed","eventType":"TransferApplicationSubmittedEvent","mappingId":"transfer-application-submitted-v1"}]}""",
            notes:
            [
                "Non-secret",
                "Saved with Target=Shared so the API runtime can read it",
                "Triggers: ApplicationSubmitted, FileUploaded",
                "Each binding needs a matching EventMappings entry for the template",
                "ScanRequestedEvent is published by the platform and cannot be configured here"
            ],
            requiresObject: true),
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
