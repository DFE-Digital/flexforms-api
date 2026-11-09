using System.Text.Json;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin;

/// <summary>
/// The smallest set of TenantConfig settings a new tenant needs before a user can sign in and
/// reach the dashboard, plus <see cref="OptionalCategories"/> the New tenant page may copy across.
/// Anything else (Layout, InternalServiceAuth, …) is added later in Tenant Settings.
/// </summary>
public static class NewTenantSettingsRequirements
{
    public const string ConnectionStrings = "ConnectionStrings";
    public const string Authorization = "Authorization";
    public const string AzureAd = "AzureAd";
    public const string Authentication = "Authentication";
    public const string DfESignIn = "DfESignIn";
    public const string EntraSso = "EntraSso";
    public const string TestAuthentication = "TestAuthentication";
    public const string TokenRefresh = "TokenRefresh";
    public const string Email = "Email";
    public const string EmailTemplates = "EmailTemplates";

    public const int MinimumSigningKeyLength = 32;

    private const string TestAuthPasswordEmailType = "TestAuthPasswordEmail";

    /// <summary>
    /// Not needed to run, but usually configured before go-live, so the New tenant page copies
    /// them from the current tenant. <see cref="Validate"/> does not check them.
    /// </summary>
    public static readonly IReadOnlySet<string> OptionalCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "FeatureManagement",
        "FileStorage",
        "Template",
        "ApplicationTemplates",
        "NotificationService",
        "NotificationBanner",
        "MassTransit",
        "FrontendSettings",
        "FormEngine",
        "ExternalApplicationsApiClient",
        "CacheSettings",
        "ApplicationTerminology",
        "ApplicationReference"
    };

    /// <summary>Every category the New tenant flow may write.</summary>
    public static readonly IReadOnlySet<string> Categories = new HashSet<string>(
        new[]
        {
            ConnectionStrings,
            Authorization,
            AzureAd,
            Authentication,
            DfESignIn,
            EntraSso,
            TestAuthentication,
            TokenRefresh,
            Email,
            EmailTemplates
        }.Concat(OptionalCategories),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns one message per missing or inconsistent requirement; empty when the tenant can run.
    /// </summary>
    public static IReadOnlyList<string> Validate(IReadOnlyCollection<NewTenantSetting> settings)
    {
        var api = BuildView(settings, "Api");
        var web = BuildView(settings, "Web");
        var errors = new List<string>();

        // Every EA DbContext for the tenant throws without it.
        Require(api, "ConnectionStrings:DefaultConnection", "Api", errors);

        // Token exchange signs and validates the user JWT with these.
        RequireKey(api, "Authorization:TokenSettings:SecretKey", "Api", errors);
        Require(api, "Authorization:TokenSettings:Issuer", "Api", errors);
        Require(api, "Authorization:TokenSettings:Audience", "Api", errors);

        // ServiceCallers only accepts Web's Entra service token when it matches a service-principal
        // provider of the tenant being called, so without it nobody can exchange a token.
        if (IsBlank(api["AzureAd:TenantId"]) && IsBlank(api["AzureAd:Issuer"]))
        {
            errors.Add("AzureAd (Api or Shared) needs TenantId or Issuer so Web's service token is accepted at token exchange.");
        }

        ValidateInteractiveScheme(api, web, errors);
        ValidateTokenRefresh(web, errors);

        return errors;
    }

    private static void ValidateInteractiveScheme(IConfiguration api, IConfiguration web, List<string> errors)
    {
        if (IsBlank(web["Authentication:Scheme"]))
        {
            errors.Add("Authentication:Scheme (Web or Shared) is required: DfESignIn, EntraSso or TestAuthentication.");
            return;
        }

        switch (TenantInteractiveAuthSchemeResolver.ResolveSchemeName(web))
        {
            case DfESignIn:
                Require(web, "DfESignIn:Authority", "Web", errors);
                Require(web, "DfESignIn:ClientId", "Web", errors);
                Require(web, "DfESignIn:ClientSecret", "Web", errors);
                if (IsBlank(api["DfESignIn:Authority"]) && IsBlank(api["DfESignIn:Issuer"]))
                    errors.Add("DfESignIn (Api or Shared) needs Authority or Issuer so the API can validate the sign-in token.");
                break;

            case EntraSso:
                Require(web, "EntraSso:TenantId", "Web", errors);
                Require(web, "EntraSso:ClientId", "Web", errors);
                if (IsBlank(api["EntraSso:TenantId"]) && IsBlank(api["EntraSso:Issuer"]))
                    errors.Add("EntraSso (Api or Shared) needs TenantId or Issuer so the API can validate the sign-in token.");
                break;

            case TestAuthentication:
                ValidateTestAuthentication(api, web, errors);
                break;

            default:
                errors.Add("Authentication:Scheme must be DfESignIn, EntraSso or TestAuthentication.");
                break;
        }
    }

    private static void ValidateTestAuthentication(IConfiguration api, IConfiguration web, List<string> errors)
    {
        foreach (var (view, label) in new[] { (api, "Api"), (web, "Web") })
        {
            if (!bool.TryParse(view["TestAuthentication:Enabled"], out var enabled) || !enabled)
                errors.Add($"TestAuthentication:Enabled must be true ({label} or Shared).");
            RequireKey(view, "TestAuthentication:JwtSigningKey", label, errors);
            Require(view, "TestAuthentication:JwtIssuer", label, errors);
            Require(view, "TestAuthentication:JwtAudience", label, errors);
        }

        // Web mints the test JWT and the API validates it, so both sides must agree.
        foreach (var key in new[] { "JwtSigningKey", "JwtIssuer", "JwtAudience" })
        {
            var path = $"TestAuthentication:{key}";
            if (!IsBlank(api[path]) && !IsBlank(web[path]) && !string.Equals(api[path], web[path], StringComparison.Ordinal))
                errors.Add($"{path} must be the same for Api and Web.");
        }

        // Test sign-in emails a one-time password; without Notify and its template nobody can sign in.
        var provider = api["Email:Provider"];
        if (IsBlank(provider))
        {
            errors.Add("Email:Provider (Api or Shared) is required for the Test Authentication one-time password.");
        }
        else if (string.Equals(provider, "GovUkNotify", StringComparison.OrdinalIgnoreCase))
        {
            Require(api, "Email:GovUkNotify:ApiKey", "Api", errors);
        }

        var hasPasswordTemplate = api.GetSection(EmailTemplates).GetChildren()
            .Any(product => !IsBlank(product[TestAuthPasswordEmailType]));
        if (!hasPasswordTemplate)
        {
            errors.Add($"EmailTemplates (Api or Shared) needs a {TestAuthPasswordEmailType} template id under a product key.");
        }
    }

    /// <summary>
    /// Mirrors Web's tenant token-refresh options: Web builds and validates them on every request,
    /// whatever the sign-in scheme, so an empty value breaks the whole site for the tenant.
    /// </summary>
    private static void ValidateTokenRefresh(IConfiguration web, List<string> errors)
    {
        var tokenEndpoint = web["TokenRefresh:TokenEndpoint"];
        var introspectionEndpoint = web["TokenRefresh:IntrospectionEndpoint"];
        var clientId = web["TokenRefresh:ClientId"];
        var clientSecret = web["TokenRefresh:ClientSecret"];

        if (web.GetSection(DfESignIn).Exists())
        {
            if (IsBlank(clientId))
                clientId = web["DfESignIn:ClientId"];
            if (IsBlank(clientSecret))
                clientSecret = web["DfESignIn:ClientSecret"];

            var authority = web["DfESignIn:Authority"];
            if (!IsBlank(authority))
            {
                var authorityUri = authority!.TrimEnd('/');
                tokenEndpoint ??= $"{authorityUri}/token";
                introspectionEndpoint ??= $"{authorityUri}/token/introspection";
            }
        }

        if (!IsAbsoluteUri(tokenEndpoint)
            || !IsAbsoluteUri(introspectionEndpoint)
            || IsBlank(clientId)
            || IsBlank(clientSecret))
        {
            errors.Add(
                "Web needs TokenRefresh (TokenEndpoint, IntrospectionEndpoint, ClientId, ClientSecret), " +
                "or a DfESignIn Authority, ClientId and ClientSecret to derive them.");
        }
    }

    private static void Require(IConfiguration view, string key, string target, List<string> errors)
    {
        if (IsBlank(view[key]))
            errors.Add($"{key} ({target} or Shared) is required.");
    }

    private static void RequireKey(IConfiguration view, string key, string target, List<string> errors)
    {
        var value = view[key];
        if (IsBlank(value))
            errors.Add($"{key} ({target} or Shared) is required.");
        else if (value!.Length < MinimumSigningKeyLength)
            errors.Add($"{key} ({target} or Shared) must be at least {MinimumSigningKeyLength} characters.");
    }

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

    private static bool IsAbsoluteUri(string? value) =>
        !IsBlank(value) && Uri.TryCreate(value, UriKind.Absolute, out _);

    private static IConfiguration BuildView(IEnumerable<NewTenantSetting> settings, string target)
    {
        var pairs = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var setting in settings
                     .Where(s => TenantSettingTargetOverlap.IsShared(s.Target)
                                 || string.Equals(s.Target, target, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(s => s.Target, StringComparer.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(setting.SettingsJson);
                Flatten(setting.Category, doc.RootElement, pairs);
            }
            catch (JsonException)
            {
                // Invalid JSON is reported by TenantSettingJsonValidator.
            }
        }

        return new ConfigurationBuilder().AddInMemoryCollection(pairs).Build();
    }

    private static void Flatten(string prefix, JsonElement element, IDictionary<string, string?> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    Flatten($"{prefix}:{property.Name}", property.Value, result);
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                    Flatten($"{prefix}:{index++}", item, result);
                break;

            case JsonValueKind.Null:
                result[prefix] = null;
                break;

            default:
                result[prefix] = element.ToString();
                break;
        }
    }
}
