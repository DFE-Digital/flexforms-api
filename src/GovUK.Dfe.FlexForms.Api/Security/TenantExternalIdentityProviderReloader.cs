using System.Diagnostics.CodeAnalysis;
using GovUK.Dfe.CoreLibs.Security.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Api.Security;

/// <summary>
/// Rebuilds <see cref="IMultiProviderExternalIdentityReloader"/> when tenant configuration
/// refreshes, so new tenants (and ClientId changes) apply to token exchange without a recycle.
/// Mirrors <c>DatabaseTenantAuthProviderRegistry</c> hot-reload behaviour.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class TenantExternalIdentityProviderReloader : IHostedService, IDisposable
{
    private readonly ITenantConfigurationProvider _tenantConfigurationProvider;
    private readonly ITenantConfigurationChangedNotifier _notifier;
    private readonly IMultiProviderExternalIdentityReloader _reloader;
    private readonly ILogger<TenantExternalIdentityProviderReloader> _logger;
    private bool _disposed;

    /// <summary>
    /// Creates a reloader that listens for tenant configuration changes.
    /// </summary>
    public TenantExternalIdentityProviderReloader(
        ITenantConfigurationProvider tenantConfigurationProvider,
        ITenantConfigurationChangedNotifier notifier,
        IMultiProviderExternalIdentityReloader reloader,
        ILogger<TenantExternalIdentityProviderReloader> logger)
    {
        _tenantConfigurationProvider = tenantConfigurationProvider;
        _notifier = notifier;
        _reloader = reloader;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _notifier.Changed += Reload;
        Reload();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _notifier.Changed -= Reload;
        return Task.CompletedTask;
    }

    private void Reload()
    {
        try
        {
            var providers = TenantOidcProviderBuilder.BuildProviders(
                _tenantConfigurationProvider.GetAllTenants());

            if (providers.Count == 0)
            {
                _logger.LogWarning(
                    "Tenant OIDC provider reload skipped: no DfESignIn/EntraSso providers with DiscoveryEndpoint were found.");
                return;
            }

            _reloader.ReloadProviders(providers);
            _logger.LogInformation(
                "External identity OIDC providers reloaded from tenant configuration ({Count} providers)",
                providers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload external identity OIDC providers from tenant configuration");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifier.Changed -= Reload;
    }
}
