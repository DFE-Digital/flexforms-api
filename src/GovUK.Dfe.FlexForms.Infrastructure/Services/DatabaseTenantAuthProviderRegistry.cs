using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Infrastructure.Services;

/// <summary>
/// Singleton, hot-reloadable registry of <see cref="TenantAuthProvider"/> rows projected from
/// every tenant's settings in <see cref="ITenantConfigurationProvider"/>. Subscribes to
/// <see cref="ITenantConfigurationChangedNotifier.Changed"/> and rebuilds its indexes in-place,
/// so adding a new tenant or rotating a signing key requires no service restart.
/// <para>
/// Intentionally framework-agnostic: returns pure <see cref="TenantAuthProvider"/> data. Signing
/// key resolution (which pulls in <c>Microsoft.IdentityModel.Tokens</c>) lives in
/// <see cref="GovUK.Dfe.FlexForms.Infrastructure.Security.ITenantSigningKeyResolver"/>.
/// </para>
/// <para>
/// Multiple providers may share the same <c>iss</c> (e.g. one Entra directory, many app
/// registrations). Bearer resolution uses <see cref="ResolveJwtBearerProvider"/> with the
/// resolved SaaS tenant id plus <c>aud</c> and <c>azp</c>/<c>appid</c> to pick the correct row.
/// </para>
/// </summary>
public sealed class DatabaseTenantAuthProviderRegistry : ITenantAuthProviderRegistry, IDisposable
{
    private readonly ITenantConfigurationProvider _tenantConfigurationProvider;
    private readonly ITenantConfigurationChangedNotifier _notifier;
    private readonly ILogger<DatabaseTenantAuthProviderRegistry> _logger;

    private volatile IReadOnlyDictionary<string, IReadOnlyList<TenantAuthProvider>> _providersByIssuer =
        new Dictionary<string, IReadOnlyList<TenantAuthProvider>>(StringComparer.OrdinalIgnoreCase);

    private volatile IReadOnlyDictionary<string, TenantAuthProvider> _byApiKeyHash
        = new Dictionary<string, TenantAuthProvider>(StringComparer.OrdinalIgnoreCase);

    private volatile IReadOnlyDictionary<string, TenantAuthProvider> _byThumbprint
        = new Dictionary<string, TenantAuthProvider>(StringComparer.OrdinalIgnoreCase);

    private volatile IReadOnlyCollection<TenantAuthProvider> _all = Array.Empty<TenantAuthProvider>();

    public DatabaseTenantAuthProviderRegistry(
        ITenantConfigurationProvider tenantConfigurationProvider,
        ITenantConfigurationChangedNotifier notifier,
        ILogger<DatabaseTenantAuthProviderRegistry> logger)
    {
        _tenantConfigurationProvider = tenantConfigurationProvider;
        _notifier = notifier;
        _logger = logger;

        _notifier.Changed += Rebuild;

        // Initial build (synchronous - the configuration provider's startup is already complete by
        // the time DI resolves this registry).
        Rebuild();
    }

    /// <inheritdoc />
    public TenantAuthProvider? GetByIssuer(string issuer)
        => GetProvidersByIssuer(issuer).FirstOrDefault();

    /// <inheritdoc />
    public IReadOnlyList<TenantAuthProvider> GetProvidersByIssuer(string issuer)
    {
        if (string.IsNullOrEmpty(issuer))
        {
            return Array.Empty<TenantAuthProvider>();
        }

        return _providersByIssuer.TryGetValue(issuer, out var list)
            ? list
            : Array.Empty<TenantAuthProvider>();
    }

    /// <inheritdoc />
    public bool HasAnyProviderForIssuer(string issuer)
        => GetProvidersByIssuer(issuer).Count > 0;

    /// <inheritdoc />
    public TenantAuthProvider? ResolveJwtBearerProvider(
        string issuer,
        IEnumerable<string> tokenAudiences,
        Guid resolvedTenantId,
        string? azpOrAppId)
    {
        if (string.IsNullOrEmpty(issuer))
        {
            return null;
        }

        var tokenAudList = tokenAudiences as IReadOnlyList<string> ?? tokenAudiences.ToList();
        var candidates = _all
            .Where(p =>
                !string.IsNullOrEmpty(p.Issuer) &&
                p.TenantId == resolvedTenantId &&
                issuer.Equals(p.Issuer, StringComparison.OrdinalIgnoreCase) &&
                AudienceMatches(p, tokenAudList))
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        if (!string.IsNullOrEmpty(azpOrAppId))
        {
            var byClient = candidates
                .Where(p =>
                    !string.IsNullOrEmpty(p.ClientId) &&
                    p.ClientId.Equals(azpOrAppId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (byClient.Count == 1)
            {
                return byClient[0];
            }
        }

        return null;
    }

    /// <inheritdoc />
    public TenantAuthProvider? GetFirstSigningProviderForIssuer(string issuer)
    {
        if (string.IsNullOrEmpty(issuer))
        {
            return null;
        }

        foreach (var p in _all)
        {
            if (string.IsNullOrEmpty(p.Issuer) || !issuer.Equals(p.Issuer, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (p.Kind == TenantAuthProviderKind.JwtHmac && !string.IsNullOrEmpty(p.SigningKey))
            {
                return p;
            }

            if (p.Kind is TenantAuthProviderKind.Oidc or TenantAuthProviderKind.EntraOidc
                && !string.IsNullOrEmpty(p.DiscoveryEndpoint))
            {
                return p;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public bool IsValidAudience(string issuer, IEnumerable<string> audiences)
        => IsJwtAudienceValidForIssuerAnyTenant(issuer, audiences);

    /// <inheritdoc />
    public bool IsJwtAudienceValidForTenant(
        string issuer,
        IEnumerable<string> tokenAudiences,
        Guid resolvedTenantId,
        string? azpOrAppId)
        => ResolveJwtBearerProvider(issuer, tokenAudiences, resolvedTenantId, azpOrAppId) is not null;

    /// <inheritdoc />
    public bool IsJwtAudienceValidForIssuerAnyTenant(string issuer, IEnumerable<string> tokenAudiences)
    {
        if (string.IsNullOrEmpty(issuer))
        {
            return false;
        }

        var tokenAudList = tokenAudiences as IReadOnlyList<string> ?? tokenAudiences.ToList();
        return _all.Any(p =>
            !string.IsNullOrEmpty(p.Issuer) &&
            issuer.Equals(p.Issuer, StringComparison.OrdinalIgnoreCase) &&
            AudienceMatches(p, tokenAudList));
    }

    /// <inheritdoc />
    public TenantAuthProvider? GetByApiKeyHash(string hashedKey)
        => _byApiKeyHash.TryGetValue(hashedKey, out var provider) ? provider : null;

    /// <inheritdoc />
    public TenantAuthProvider? GetByCertificateThumbprint(string thumbprint)
        => _byThumbprint.TryGetValue(NormalizeThumbprint(thumbprint), out var provider) ? provider : null;

    /// <inheritdoc />
    public IReadOnlyCollection<TenantAuthProvider> GetAll() => _all;

    private static bool AudienceMatches(TenantAuthProvider provider, IReadOnlyList<string> tokenAudiences)
    {
        if (provider.Audiences is null || provider.Audiences.Count == 0)
        {
            return false;
        }

        foreach (var ta in tokenAudiences)
        {
            if (string.IsNullOrEmpty(ta))
            {
                continue;
            }

            foreach (var configured in provider.Audiences)
            {
                if (string.Equals(ta, configured, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void Rebuild()
    {
        try
        {
            var tenants = _tenantConfigurationProvider.GetAllTenants();

            var byIssuer = new Dictionary<string, List<TenantAuthProvider>>(StringComparer.OrdinalIgnoreCase);
            var byApiKey = new Dictionary<string, TenantAuthProvider>(StringComparer.OrdinalIgnoreCase);
            var byThumb = new Dictionary<string, TenantAuthProvider>(StringComparer.OrdinalIgnoreCase);
            var all = new List<TenantAuthProvider>();

            foreach (var tenant in tenants)
            {
                foreach (var provider in ProjectTenantProviders(tenant))
                {
                    all.Add(provider);

                    if (!string.IsNullOrEmpty(provider.Issuer))
                    {
                        if (!byIssuer.TryGetValue(provider.Issuer, out var issuerList))
                        {
                            issuerList = new List<TenantAuthProvider>();
                            byIssuer[provider.Issuer] = issuerList;
                        }

                        issuerList.Add(provider);
                    }

                    if (!string.IsNullOrEmpty(provider.ApiKeyHash))
                    {
                        if (byApiKey.TryGetValue(provider.ApiKeyHash, out var existingByKey)
                            && existingByKey.TenantId != provider.TenantId)
                        {
                            _logger.LogError(
                                "Duplicate ApiKeyHash detected across tenants during registry rebuild: " +
                                "tenants {ExistingTenantId} and {NewTenantId}. Keeping first registration; " +
                                "the second tenant's API key will not authenticate until the collision is fixed.",
                                existingByKey.TenantId,
                                provider.TenantId);
                        }
                        else
                        {
                            byApiKey[provider.ApiKeyHash] = provider;
                        }
                    }

                    if (!string.IsNullOrEmpty(provider.CertificateThumbprint))
                    {
                        var thumb = NormalizeThumbprint(provider.CertificateThumbprint);
                        if (byThumb.TryGetValue(thumb, out var existingByThumb)
                            && existingByThumb.TenantId != provider.TenantId)
                        {
                            _logger.LogError(
                                "Duplicate certificate thumbprint detected across tenants during registry rebuild: " +
                                "tenants {ExistingTenantId} and {NewTenantId} (thumbprint {Thumbprint}). " +
                                "Keeping first registration; the second tenant's cert will not authenticate until fixed.",
                                existingByThumb.TenantId,
                                provider.TenantId,
                                thumb);
                        }
                        else
                        {
                            byThumb[thumb] = provider;
                        }
                    }
                }
            }

            var frozenByIssuer = byIssuer.ToDictionary(
                static kv => kv.Key,
                static kv => (IReadOnlyList<TenantAuthProvider>)kv.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase);

            _providersByIssuer = frozenByIssuer;
            _byApiKeyHash = byApiKey;
            _byThumbprint = byThumb;
            _all = all;

            var issuerSlotCount = frozenByIssuer.Values.Sum(static list => list.Count);
            _logger.LogInformation(
                "TenantAuthProviderRegistry rebuilt: {ProviderCount} providers across {TenantCount} tenants " +
                "({IssuerSlotCount} issuer slots across {DistinctIssuers} distinct issuers, {ByApiKey} by api-key, {ByThumbprint} by cert)",
                all.Count,
                tenants.Count,
                issuerSlotCount,
                frozenByIssuer.Count,
                byApiKey.Count,
                byThumb.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rebuild TenantAuthProviderRegistry. Retaining previous snapshot.");
        }
    }

    /// <summary>
    /// Projects a tenant's settings sections into a flat list of <see cref="TenantAuthProvider"/>.
    /// Prefers the explicit <c>AuthProviders</c> category when it contains at least one
    /// entry; otherwise falls back to the legacy <c>DfESignIn</c>/<c>EntraSso</c>/<c>AzureAd</c>/
    /// <c>Authorization:TokenSettings</c> sections so deployments can adopt the new shape lazily.
    /// </summary>
    private static IEnumerable<TenantAuthProvider> ProjectTenantProviders(TenantConfiguration tenant)
    {
        // 1) Preferred: explicit AuthProviders array.
        var explicitProviders = tenant.Settings.GetSection("AuthProviders:Providers");
        var explicitChildren = explicitProviders.GetChildren().ToArray();
        if (explicitChildren.Length > 0)
        {
            foreach (var providerSection in explicitChildren)
            {
                var projected = TryProjectExplicit(tenant.Id, providerSection);
                if (projected is not null)
                {
                    yield return projected;
                }
            }

            yield break;
        }

        // 2) Legacy projection (back-compat).
        // Internal HMAC-signed JWT (used by /tokens/exchange) - one per tenant.
        var internalKey = tenant.Settings["Authorization:TokenSettings:SecretKey"];
        var internalIssuer = tenant.Settings["Authorization:TokenSettings:Issuer"];
        var internalAudience = tenant.Settings["Authorization:TokenSettings:Audience"];
        if (!string.IsNullOrEmpty(internalKey) && !string.IsNullOrEmpty(internalIssuer))
        {
            yield return new TenantAuthProvider(
                TenantId: tenant.Id,
                Name: "internal-user",
                Kind: TenantAuthProviderKind.JwtHmac,
                IsServicePrincipal: false,
                Issuer: internalIssuer,
                Audiences: string.IsNullOrEmpty(internalAudience)
                    ? Array.Empty<string>()
                    : new[] { internalAudience },
                SigningKey: internalKey);
        }

        // DfE Sign-In (generic OIDC, user-facing).
        var dsiIssuer = tenant.Settings["DfESignIn:Issuer"]
            ?? tenant.Settings["DfESignIn:Authority"];
        var dsiAuthority = tenant.Settings["DfESignIn:Authority"];
        var dsiClientId = tenant.Settings["DfESignIn:ClientId"];
        var dsiDiscovery = tenant.Settings["DfESignIn:DiscoveryEndpoint"]
            ?? (string.IsNullOrEmpty(dsiAuthority) ? null : dsiAuthority.TrimEnd('/') + "/.well-known/openid-configuration");
        if (!string.IsNullOrEmpty(dsiIssuer))
        {
            yield return new TenantAuthProvider(
                TenantId: tenant.Id,
                Name: "dsi",
                Kind: TenantAuthProviderKind.Oidc,
                IsServicePrincipal: false,
                Issuer: dsiIssuer,
                Authority: dsiAuthority,
                DiscoveryEndpoint: dsiDiscovery,
                ClientId: dsiClientId,
                Audiences: string.IsNullOrEmpty(dsiClientId) ? Array.Empty<string>() : new[] { dsiClientId });
        }

        // Entra SSO (Entra OIDC, user-facing).
        var entraSsoTenant = tenant.Settings["EntraSso:TenantId"];
        var entraSsoClient = tenant.Settings["EntraSso:ClientId"];
        var entraSsoIssuer = tenant.Settings["EntraSso:Issuer"]
            ?? (string.IsNullOrEmpty(entraSsoTenant) ? null : $"https://login.microsoftonline.com/{entraSsoTenant}/v2.0");
        var entraSsoDiscovery = tenant.Settings["EntraSso:DiscoveryEndpoint"]
            ?? (string.IsNullOrEmpty(entraSsoTenant) ? null : $"https://login.microsoftonline.com/{entraSsoTenant}/v2.0/.well-known/openid-configuration");
        if (!string.IsNullOrEmpty(entraSsoIssuer))
        {
            yield return new TenantAuthProvider(
                TenantId: tenant.Id,
                Name: "entra-sso",
                Kind: TenantAuthProviderKind.EntraOidc,
                IsServicePrincipal: false,
                Issuer: entraSsoIssuer,
                DiscoveryEndpoint: entraSsoDiscovery,
                ClientId: entraSsoClient,
                Audiences: string.IsNullOrEmpty(entraSsoClient) ? Array.Empty<string>() : new[] { entraSsoClient });
        }

        // Azure AD (Entra app, service-to-service callers).
        var azureAdTenant = tenant.Settings["AzureAd:TenantId"];
        var azureAdAudience = tenant.Settings["AzureAd:Audience"]
            ?? tenant.Settings["AzureAd:ClientId"];
        var azureAdIssuer = tenant.Settings["AzureAd:Issuer"]
            ?? (string.IsNullOrEmpty(azureAdTenant) ? null : $"https://sts.windows.net/{azureAdTenant}/");
        var azureAdDiscovery = tenant.Settings["AzureAd:DiscoveryEndpoint"]
            ?? (string.IsNullOrEmpty(azureAdTenant) ? null : $"https://login.microsoftonline.com/{azureAdTenant}/v2.0/.well-known/openid-configuration");
        if (!string.IsNullOrEmpty(azureAdIssuer))
        {
            var azureAdProvider = new TenantAuthProvider(
                TenantId: tenant.Id,
                Name: "azure-ad-svc",
                Kind: TenantAuthProviderKind.EntraOidc,
                IsServicePrincipal: true,
                Issuer: azureAdIssuer,
                DiscoveryEndpoint: azureAdDiscovery,
                ClientId: tenant.Settings["AzureAd:ClientId"],
                Audiences: string.IsNullOrEmpty(azureAdAudience) ? Array.Empty<string>() : new[] { azureAdAudience });
            yield return azureAdProvider;

            // v2 access tokens often use login.microsoftonline.com/{tid}/v2.0 while legacy config defaults to sts.windows.net.
            if (!string.IsNullOrEmpty(azureAdTenant))
            {
                var v2Issuer = $"https://login.microsoftonline.com/{azureAdTenant}/v2.0";
                if (!v2Issuer.Equals(azureAdIssuer, StringComparison.OrdinalIgnoreCase))
                {
                    yield return azureAdProvider with { Issuer = v2Issuer };
                }
            }
        }
    }

    /// <summary>
    /// Projects a single entry from the explicit <c>AuthProviders:Providers:n</c> section into a
    /// <see cref="TenantAuthProvider"/>. Returns null for entries with an unknown or missing
    /// <c>Kind</c>.
    /// </summary>
    private static TenantAuthProvider? TryProjectExplicit(Guid tenantId, IConfigurationSection section)
    {
        var kindStr = section["Kind"];
        if (!Enum.TryParse<TenantAuthProviderKind>(kindStr, ignoreCase: true, out var kind))
        {
            return null;
        }

        var name = section["Name"] ?? kindStr!;
        var isSvc = bool.TryParse(section["IsServicePrincipal"], out var svc) && svc;
        var audiences = section.GetSection("Audiences").Get<string[]>()
            ?? (string.IsNullOrEmpty(section["Audience"])
                ? Array.Empty<string>()
                : new[] { section["Audience"]! });
        var roles = section.GetSection("Roles").Get<string[]>();

        return kind switch
        {
            TenantAuthProviderKind.JwtHmac => new TenantAuthProvider(
                TenantId: tenantId,
                Name: name,
                Kind: kind,
                IsServicePrincipal: isSvc,
                Issuer: section["Issuer"],
                Audiences: audiences,
                SigningKey: section["SigningKey"],
                Roles: roles),

            TenantAuthProviderKind.Oidc or TenantAuthProviderKind.EntraOidc => new TenantAuthProvider(
                TenantId: tenantId,
                Name: name,
                Kind: kind,
                IsServicePrincipal: isSvc,
                Issuer: section["Issuer"],
                Authority: section["Authority"],
                DiscoveryEndpoint: section["DiscoveryEndpoint"]
                    ?? (string.IsNullOrEmpty(section["Authority"])
                        ? null
                        : section["Authority"]!.TrimEnd('/') + "/.well-known/openid-configuration"),
                ClientId: section["ClientId"],
                Audiences: audiences,
                Roles: roles),

            TenantAuthProviderKind.ApiKey => new TenantAuthProvider(
                TenantId: tenantId,
                Name: name,
                Kind: kind,
                IsServicePrincipal: isSvc,
                ApiKeyHash: section["KeyHash"],
                Roles: roles),

            TenantAuthProviderKind.Mtls => new TenantAuthProvider(
                TenantId: tenantId,
                Name: name,
                Kind: kind,
                IsServicePrincipal: isSvc,
                CertificateThumbprint: section["Thumbprint"],
                Roles: roles),

            _ => null
        };
    }

    /// <summary>
    /// Normalizes a certificate thumbprint to uppercase hex without separators so lookups are stable.
    /// </summary>
    private static string NormalizeThumbprint(string thumbprint)
        => thumbprint.Replace(":", "").Replace(" ", "").ToUpperInvariant();

    public void Dispose()
    {
        _notifier.Changed -= Rebuild;
    }
}
