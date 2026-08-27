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
            notes: ["Forced secret category", "SuperAdmin only", "Named connections (not only DefaultConnection) are allowed"],
            requiresObject: true),

        Entry(
            "FileStorage",
            "Required per-tenant file storage. On Container Apps with an Azure Files mount, use Hybrid.",
            ["Api", "Shared"],
            example: """{"Provider":"Hybrid","Local":{"BaseDirectory":"/uploads","AllowedExtensions":["jpg","png","pdf"],"MaxFileSizeBytes":10000000},"Azure":{"ConnectionString":"...","ShareName":"uploads"}}""",
            notes:
            [
                "Forced secret category",
                "SuperAdmin only",
                "Host GlobalConfiguration:FileStorage may be Local with a dummy path — boot only",
                "Hybrid (recommended on Container Apps): File Share is mounted at Local.BaseDirectory (e.g. /uploads). App writes 'locally' to the mount; files appear on the share. SAS uses tenant Azure ConnectionString + ShareName",
                "Local — disk only (dev). Tenant Local.BaseDirectory required",
                "Azure — Azure SDK upload/download (no mount). Prefer Hybrid when using a volume mount",
                "Deleting this category breaks file upload/download/delete for the tenant"
            ],
            requiresObject: true),

        Entry(
            "Email",
            "Required per-tenant GOV.UK Notify settings (API key, support address). Missing/incomplete config fails that tenant's email ops.",
            ["Api", "Shared"],
            example: """{"Provider":"GovUkNotify","ServiceSupportEmailAddress":"support@education.gov.uk","GovUkNotify":{"ApiKey":"...","BaseUrl":"https://api.notifications.service.gov.uk","TimeoutSeconds":30}}""",
            notes:
            [
                "Forced secret category",
                "SuperAdmin only",
                "Host GlobalConfiguration:Email registers IEmailService only — runtime does not fall back to host",
                "Requires Email.Provider and (for GovUkNotify) GovUkNotify.ApiKey; feedback also needs ServiceSupportEmailAddress",
                "Deleting this category breaks outbound email for the tenant"
            ],
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
            "ApplicationTemplates",
            "API HostMappings / template GUIDs for the tenant catalogue and email resolution.",
            ["Api", "Shared"],
            example: """{"HostMappings":{"transfers":"9A4E9C58-9135-468C-B154-7B966F7ACFB7"}}""",
            notes:
            [
                "SuperAdmin only",
                "Each GUID must exist in EA and be legacy (TenantId null) or owned by this tenant",
                "Foreign TenantId GUIDs are rejected on save and ignored at runtime"
            ],
            requiresObject: true),

        Entry(
            "Template",
            "Web HostMappings / default template Id for hostname → TemplateId session resolution.",
            ["Web"],
            example: """{"HostMappings":{"transfers.dev-flexforms.rsd.education.gov.uk":"9A4E9C58-9135-468C-B154-7B966F7ACFB7"},"Id":"9A4E9C58-9135-468C-B154-7B966F7ACFB7"}""",
            notes:
            [
                "SuperAdmin only",
                "Same ownership rules as ApplicationTemplates"
            ],
            requiresObject: true),

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
            "Application listing page size, filters, and dashboard display text (delegated to Tenant Admins).",
            ["Web"],
            example: """{"PageSize":50,"EnableApplicationFilters":false,"MainHeading":"Your applications","InProgressHeading":"Applications in progress","StartNewHeading":"Start a new application","StartNewHint":"If you start an application, you will be the lead applicant for it.","StartNewButtonText":"Start new application"}""",
            notes:
            [
                "Non-secret",
                "Also editable via Organisation Settings",
                "Text fields are optional; leave blank to use ApplicationTerminology-based defaults"
            ],
            requiresObject: true),

        Entry(
            "ApplicationPreview",
            "Check-your-answers page heading and submit-section copy (delegated to Tenant Admins).",
            ["Web"],
            example: """{"PageHeading":"Check your answers","SubmitHeading":"Submit your application","SubmitHint":"By submitting this application you are confirming that, to the best of your knowledge, the details you are providing are correct.","SubmitButtonText":"Submit","HideSubmitSection":false}""",
            notes:
            [
                "Non-secret",
                "Also editable via Organisation Settings",
                "Text fields are optional; leave blank to use ApplicationTerminology-based defaults",
                "HideSubmitSection removes the whole submit block on the preview page"
            ],
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
            "EmailPlaceholderMappings",
            "Per-template field mappings for GOV.UK Notify email personalisation placeholders.",
            ["Shared"],
            example: """{"form-001":{"ApplicationSubmitted":{"mappingId":"transfer-submitted-email-v1","eventType":"ApplicationSubmitted","fieldMappings":{"AcademyName":{"sourceType":"ComplexFieldProperty","sourceFieldId":"academiesSearch","nestedPath":"name"},"user_full_name":{"sourceType":"Metadata","sourceFieldId":"submittedByFullName}}}}}""",
            notes:
            [
                "Non-secret",
                "Saved with Target=Shared so the API runtime can read it",
                "Also editable via Organisation / safe TenantConfig settings",
                "Keys under each email type are Notify personalisation placeholders (e.g. AcademyName → ((AcademyName)))",
                "Email types: ApplicationSubmitted, ContributorInvited, ContributorAccessGranted",
                "Uses the same fieldMappings DSL as EventMappings (DirectField, ComplexFieldProperty, Collection, Metadata, etc.)",
                "Baseline personalisation keys are always sent; configured mappings overlay/add placeholders"
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
            "SelfRegistration",
            "Default form granted when a user auto-registers and more than one template is live.",
            ["Shared"],
            example: """{"DefaultTemplateId":"00000000-0000-0000-0000-000000000000"}""",
            notes:
            [
                "Non-secret",
                "Saved with Target=Shared so the API can read it during register and token exchange",
                "Zero live templates: no form access",
                "Exactly one live template: that form is granted (this setting is ignored)",
                "Several live templates: grant DefaultTemplateId if it is live; otherwise grant nothing and an admin assigns forms later",
                "ExternalApplicationsApiClient:DefaultTemplateId is also honoured if set on this tenant"
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
