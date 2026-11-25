using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace GovUK.Dfe.FlexForms.Api.Security;

/// <summary>
/// The request-time rules that tie an authenticated caller to the resolved tenant: which scheme a request
/// uses, whether a bearer token's audience fits the tenant, whether its <c>tenant_id</c> matches, and which
/// tenant a client certificate belongs to. <see cref="AuthorizationExtensions"/> wires these into the
/// authentication handlers; keeping them here lets them be tested without building the auth pipeline.
/// </summary>
internal static class TenantAuthenticationRules
{
    /// <summary>
    /// The <c>CompositeScheme</c> dispatcher. Bearer tokens (user JWTs and Entra service tokens) go through
    /// the single dynamic TenantBearer scheme, which looks the provider up in <see cref="ITenantAuthProviderRegistry"/>.
    /// </summary>
    public static string SelectScheme(HttpContext context)
    {
        // Hubs authorise with the hubauth cookie only. On reconnect the SignalR JS client
        // re-sends the Azure SignalR access token as a bearer, which TenantBearer would reject.
        if (context.Request.Path.StartsWithSegments("/hubs"))
            return "HubCookie";
        if (context.Request.Headers.ContainsKey(AuthConstants.ApiKeyHeader))
            return AuthConstants.ApiKey;
        if (context.Connection.ClientCertificate is not null)
            return AuthConstants.Mtls;
        if (AuthorizationExtensions.EndpointRequiresPlatformBearerOnly(context))
            return AuthConstants.PlatformBearer;
        return AuthConstants.TenantBearer;
    }

    /// <summary>
    /// TenantBearer audience validation. With a resolved tenant the audience (and <c>azp</c>/<c>appid</c>)
    /// must fit one of that tenant's providers; without one, any tenant's provider for the issuer will do.
    /// </summary>
    public static bool IsAudienceValid(
        IEnumerable<string> audiences,
        SecurityToken? securityToken,
        ITenantAuthProviderRegistry registry,
        ITenantContextAccessor? tenantAccessor)
    {
        var issuer = securityToken?.Issuer;
        if (string.IsNullOrEmpty(issuer))
        {
            return false;
        }

        var jwt = securityToken as JwtSecurityToken;
        var azpOrAppId = PickAzpOrAppId(jwt);
        var tenantId = tenantAccessor?.CurrentTenant?.Id;
        if (tenantId is null)
        {
            return registry.IsJwtAudienceValidForIssuerAnyTenant(issuer, audiences);
        }

        return registry.IsJwtAudienceValidForTenant(issuer, audiences, tenantId.Value, azpOrAppId);
    }

    /// <summary>
    /// Enforces that a successfully-validated bearer token's <c>tenant_id</c> claim matches
    /// the request's resolved tenant (from <see cref="ITenantContextAccessor"/>). For Entra
    /// service tokens that don't carry <c>tenant_id</c>, the matched provider is resolved via
    /// <see cref="ITenantAuthProviderRegistry.ResolveJwtBearerProvider"/> (issuer + aud +
    /// <c>azp</c>/<c>appid</c> + current tenant) so many SaaS tenants can share one Entra directory.
    /// </summary>
    public static Task EnforceTenantConsistencyAsync(TokenValidatedContext context)
    {
        var tenantAccessor = context.HttpContext.RequestServices.GetService<ITenantContextAccessor>();
        var currentTenant = tenantAccessor?.CurrentTenant;

        // No tenant resolved (e.g. health/swagger endpoints) - allow.
        if (currentTenant is null) return Task.CompletedTask;

        var registry = context.HttpContext.RequestServices.GetService<ITenantAuthProviderRegistry>();
        var issuer = context.SecurityToken?.Issuer;
        var jwt = context.SecurityToken as JwtSecurityToken;
        var tokenAudiences = jwt?.Audiences ?? ExtractAudiencesFromPrincipal(context.Principal);
        var azpOrAppId = PickAzpOrAppId(jwt) ?? PickAzpOrAppIdFromPrincipal(context.Principal);

        TenantAuthProvider? matchedProvider = null;
        if (!string.IsNullOrEmpty(issuer) && registry is not null)
        {
            matchedProvider = registry.ResolveJwtBearerProvider(
                issuer,
                tokenAudiences,
                currentTenant.Id,
                azpOrAppId);
        }

        // Stash the matched provider for the ServicePrincipalRequirement handler.
        if (matchedProvider is not null)
        {
            TenantAuthPrincipalFactory.StashProvider(context.HttpContext, matchedProvider);
        }

        var tokenTenantClaim = context.Principal?.Claims
            .FirstOrDefault(c => c.Type == TenantAuthClaimTypes.TenantId)?.Value;

        if (!string.IsNullOrEmpty(tokenTenantClaim))
        {
            if (!string.Equals(tokenTenantClaim, currentTenant.Id.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                context.Fail($"Token tenant_id '{tokenTenantClaim}' does not match the resolved tenant '{currentTenant.Name}' ({currentTenant.Id}).");
            }
            return Task.CompletedTask;
        }

        if (matchedProvider is null && !string.IsNullOrEmpty(issuer) && registry?.HasAnyProviderForIssuer(issuer) == true)
        {
            context.Fail(
                $"No unique auth provider for issuer '{issuer}' and tenant '{currentTenant.Name}' ({currentTenant.Id}). " +
                "Ensure token aud matches configured audiences and azp/appid matches AzureAd:ClientId (or AuthProviders ClientId) for this tenant.");
            return Task.CompletedTask;
        }

        if (matchedProvider is null)
        {
            // Legacy/unknown issuer - log warning. Hard-fail once we are confident no legacy
            // tokens remain in circulation.
            var logger = context.HttpContext.RequestServices.GetService<ILogger<Program>>();
            logger?.LogWarning(
                "TenantBearer token issuer '{Issuer}' has no matching provider in the registry. " +
                "Tenant {TenantId} ({TenantName}). This grace path will be removed in a future release.",
                issuer, currentTenant.Id, currentTenant.Name);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles a validated client certificate: looks up the matching <see cref="TenantAuthProvider"/>
    /// by thumbprint, fails the context if there's no match, otherwise replaces the principal
    /// with a uniform tenant principal built by <see cref="TenantAuthPrincipalFactory"/>.
    /// </summary>
    public static Task OnCertificateValidated(CertificateValidatedContext ctx)
    {
        var registry = ctx.HttpContext.RequestServices.GetRequiredService<ITenantAuthProviderRegistry>();
        var provider = registry.GetByCertificateThumbprint(ctx.ClientCertificate.Thumbprint);
        if (provider is null)
        {
            ctx.Fail("Unknown client certificate.");
            return Task.CompletedTask;
        }

        var tenantAccessor = ctx.HttpContext.RequestServices.GetService<ITenantContextAccessor>();
        var currentTenant = tenantAccessor?.CurrentTenant;
        if (currentTenant is not null && provider.TenantId != currentTenant.Id)
        {
            var logger = ctx.HttpContext.RequestServices.GetService<ILogger<Program>>();
            logger?.LogWarning(
                "Client certificate belongs to tenant {ProviderTenantId} but request resolved to {ResolvedTenantId} ({ResolvedTenantName}).",
                provider.TenantId,
                currentTenant.Id,
                currentTenant.Name);

            ctx.Fail(
                $"Client certificate tenant '{provider.TenantId}' does not match the resolved tenant '{currentTenant.Name}' ({currentTenant.Id}).");
            return Task.CompletedTask;
        }

        TenantAuthPrincipalFactory.StashProvider(ctx.HttpContext, provider);
        // Preserve any claims the certificate handler already projected (subject, thumbprint, etc.)
        var existingClaims = ctx.Principal?.Claims ?? Enumerable.Empty<Claim>();
        ctx.Principal = TenantAuthPrincipalFactory.BuildPrincipal(provider, AuthConstants.Mtls, existingClaims);
        ctx.Success();
        return Task.CompletedTask;
    }

    /// <summary>Reads <c>aud</c> from the validated principal when the security token is not a <see cref="JwtSecurityToken"/>.</summary>
    private static IEnumerable<string> ExtractAudiencesFromPrincipal(ClaimsPrincipal? principal)
    {
        if (principal is null) yield break;
        foreach (var c in principal.FindAll(JwtRegisteredClaimNames.Aud))
        {
            if (!string.IsNullOrEmpty(c.Value))
            {
                yield return c.Value;
            }
        }
    }

    /// <summary>Resolves Entra <c>azp</c> / <c>appid</c> from a JWT for per-app registration matching.</summary>
    private static string? PickAzpOrAppId(JwtSecurityToken? jwt)
    {
        if (jwt is null) return null;
        var azp = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Azp)?.Value;
        if (!string.IsNullOrEmpty(azp)) return azp;
        var appid = jwt.Claims.FirstOrDefault(c => c.Type == "appid")?.Value;
        if (!string.IsNullOrEmpty(appid)) return appid;
        return jwt.Claims.FirstOrDefault(c =>
                c.Type == "http://schemas.microsoft.com/identity/claims/appid")
            ?.Value;
    }

    private static string? PickAzpOrAppIdFromPrincipal(ClaimsPrincipal? principal)
        => EntraClientIdentity.PickClientId(principal);
}
