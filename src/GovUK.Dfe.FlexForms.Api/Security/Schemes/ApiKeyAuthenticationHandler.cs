using System.Text.Encodings.Web;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GovUK.Dfe.FlexForms.Api.Security.Schemes;

/// <summary>
/// Authenticates a request based on a shared <c>X-Api-Key</c> header. The key value is hashed
/// via the canonical <see cref="TenantApiKeyHasher.Hash(string?)"/> (Domain layer) and looked
/// up in <see cref="ITenantAuthProviderRegistry"/> - we never store or compare raw keys. On
/// success, the matched <see cref="TenantAuthProvider"/> is projected into a principal by
/// <see cref="TenantAuthPrincipalFactory"/> so this handler does not need to know which claims
/// a tenant principal carries.
/// <para>
/// When a tenant has been resolved for the request, the provider's <see cref="TenantAuthProvider.TenantId"/>
/// must match <see cref="ITenantContextAccessor.CurrentTenant"/> — same fail-closed rule as TenantBearer.
/// </para>
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    ITenantAuthProviderRegistry registry,
    ITenantContextAccessor tenantContextAccessor)
    : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, loggerFactory, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var headerName = Options.HeaderName;
        if (!Request.Headers.TryGetValue(headerName, out var rawKeyValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var rawKey = rawKeyValues.ToString();
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return Task.FromResult(AuthenticateResult.Fail($"Header {headerName} is empty."));
        }

        var hash = TenantApiKeyHasher.Hash(rawKey);
        var provider = registry.GetByApiKeyHash(hash);
        if (provider is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Unknown API key."));
        }

        var currentTenant = tenantContextAccessor.CurrentTenant;
        if (currentTenant is not null && provider.TenantId != currentTenant.Id)
        {
            Logger.LogWarning(
                "API key belongs to tenant {ProviderTenantId} but request resolved to {ResolvedTenantId} ({ResolvedTenantName}).",
                provider.TenantId,
                currentTenant.Id,
                currentTenant.Name);

            return Task.FromResult(AuthenticateResult.Fail(
                $"API key tenant '{provider.TenantId}' does not match the resolved tenant '{currentTenant.Name}' ({currentTenant.Id})."));
        }

        TenantAuthPrincipalFactory.StashProvider(Context, provider);
        var principal = TenantAuthPrincipalFactory.BuildPrincipal(provider, AuthConstants.ApiKey);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
