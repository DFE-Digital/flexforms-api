using GovUK.Dfe.CoreLibs.Security.Configurations;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;

namespace GovUK.Dfe.FlexForms.Api.Security;

/// <summary>
/// Builds CoreLibs multi-provider OIDC options from live tenant settings
/// (DfE Sign-In + enabled Entra SSO).
/// </summary>
internal static class TenantOidcProviderBuilder
{
    /// <summary>
    /// Projects every active tenant's DfESignIn / EntraSso settings into OIDC provider options
    /// for <see cref="GovUK.Dfe.CoreLibs.Security.Interfaces.IExternalIdentityValidator"/>.
    /// </summary>
    public static IReadOnlyList<OpenIdConnectOptions> BuildProviders(
        IEnumerable<TenantConfiguration> tenants)
    {
        var providers = new List<OpenIdConnectOptions>();

        foreach (var tenant in tenants)
        {
            AddDfESignInProvider(tenant, providers);
            AddEntraSsoProvider(tenant, providers);
        }

        return providers;
    }

    private static void AddDfESignInProvider(
        TenantConfiguration tenant,
        ICollection<OpenIdConnectOptions> providers)
    {
        var dfeSignInSection = tenant.Settings.GetSection("DfESignIn");
        var discoveryEndpoint = dfeSignInSection["DiscoveryEndpoint"];

        if (string.IsNullOrEmpty(discoveryEndpoint))
        {
            return;
        }

        var providerOpts = new OpenIdConnectOptions
        {
            Issuer = dfeSignInSection["Issuer"],
            Authority = dfeSignInSection["Authority"],
            ClientId = dfeSignInSection["ClientId"],
            ClientSecret = dfeSignInSection["ClientSecret"],
            DiscoveryEndpoint = discoveryEndpoint,
            ValidateIssuer = bool.TryParse(dfeSignInSection["ValidateIssuer"], out var vi) ? vi : true,
            ValidateAudience = bool.TryParse(dfeSignInSection["ValidateAudience"], out var va) ? va : true,
            ValidateLifetime = bool.TryParse(dfeSignInSection["ValidateLifetime"], out var vl) ? vl : true,
            RedirectUri = dfeSignInSection["RedirectUri"],
            Prompt = dfeSignInSection["Prompt"],
            ResponseType = dfeSignInSection["ResponseType"] ?? "code",
            RequireHttpsMetadata = bool.TryParse(dfeSignInSection["RequireHttpsMetadata"], out var rhm) ? rhm : true,
            GetClaimsFromUserInfoEndpoint = bool.TryParse(dfeSignInSection["GetClaimsFromUserInfoEndpoint"], out var gc) ? gc : true,
            SaveTokens = bool.TryParse(dfeSignInSection["SaveTokens"], out var st) ? st : true,
            UseTokenLifetime = bool.TryParse(dfeSignInSection["UseTokenLifetime"], out var utl) ? utl : true,
            NameClaimType = dfeSignInSection["NameClaimType"] ?? "email"
        };

        var scopesSection = dfeSignInSection.GetSection("Scopes");
        if (scopesSection.Exists())
        {
            providerOpts.Scopes = scopesSection.Get<List<string>>() ?? ["openid", "profile", "email"];
        }

        providers.Add(providerOpts);
    }

    private static void AddEntraSsoProvider(
        TenantConfiguration tenant,
        ICollection<OpenIdConnectOptions> providers)
    {
        var entraSsoSection = tenant.Settings.GetSection(EntraSsoOptions.SectionName);
        var entraSso = entraSsoSection.Get<EntraSsoOptions>();
        if (entraSso is not { Enabled: true } || string.IsNullOrEmpty(entraSso.TenantId))
        {
            return;
        }

        var instance = string.IsNullOrWhiteSpace(entraSso.Instance)
            ? "https://login.microsoftonline.com"
            : entraSso.Instance.TrimEnd('/');

        providers.Add(new OpenIdConnectOptions
        {
            Issuer = $"{instance}/{entraSso.TenantId}/v2.0",
            Authority = entraSso.Authority,
            ClientId = entraSso.ClientId,
            DiscoveryEndpoint = $"{instance}/{entraSso.TenantId}/v2.0/.well-known/openid-configuration",
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidIssuers =
            [
                $"{instance}/{entraSso.TenantId}/v2.0",
                $"https://sts.windows.net/{entraSso.TenantId}/",
                $"https://login.microsoftonline.com/{entraSso.TenantId}/v2.0"
            ],
            ValidAudiences =
            [
                entraSso.ClientId,
                $"api://{entraSso.ClientId}"
            ]
        });
    }
}
