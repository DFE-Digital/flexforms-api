using System.Threading.Channels;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Utils.Configuration;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GovUK.Dfe.FlexForms.Infrastructure.Messaging.Outbox;

/// <summary>
/// Runs one MassTransit <see cref="BusOutboxDeliveryService{TDbContext}"/> per distinct tenant EA
/// database. The built-in delivery service is disabled because it has no tenant context and would
/// only ever drain the first tenant's database. Tenants sharing a database share a loop.
/// The set of loops is reconciled whenever tenant configuration is refreshed.
/// </summary>
public sealed class TenantOutboxDeliveryService(
    IServiceProvider provider,
    ITenantConfigurationProvider tenantProvider,
    TenantOutboxNotificationHub notificationHub,
    IOptions<OutboxOptions> routingOptions,
    ILogger<TenantOutboxDeliveryService> logger) : BackgroundService
{
    private const string ConnectionStringName = "DefaultConnection";

    private readonly Dictionary<string, RunningDelivery> _running = new(StringComparer.OrdinalIgnoreCase);

    private readonly Channel<bool> _reconcileSignal = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var routing = routingOptions.Value;
        logger.LogInformation(
            "Transactional outbox routing: Mode {Mode}, Events [{Events}]. Other events publish directly to the bus.",
            routing.Mode,
            string.Join(", ", routing.Events));

        var changedNotifier = provider.GetService<ITenantConfigurationChangedNotifier>();
        if (changedNotifier is not null)
            changedNotifier.Changed += OnTenantConfigurationChanged;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await ReconcileAsync(stoppingToken);
                await _reconcileSignal.Reader.ReadAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (changedNotifier is not null)
                changedNotifier.Changed -= OnTenantConfigurationChanged;

            foreach (var key in _running.Keys.ToList())
                await StopDeliveryAsync(key);
        }
    }

    private void OnTenantConfigurationChanged() => _reconcileSignal.Writer.TryWrite(true);

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        var desired = tenantProvider.GetAllTenants()
            .Select(tenant => (Tenant: tenant, Connection: tenant.GetConnectionString(ConnectionStringName)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Connection))
            .GroupBy(x => x.Connection!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Tenant, StringComparer.OrdinalIgnoreCase);

        foreach (var key in _running.Keys.Where(k => !desired.ContainsKey(k)).ToList())
            await StopDeliveryAsync(key);

        foreach (var (key, tenant) in desired)
        {
            if (_running.ContainsKey(key))
                continue;

            try
            {
                await StartDeliveryAsync(key, tenant, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to start outbox delivery for tenant {TenantName}", tenant.Name);
            }
        }
    }

    private async Task StartDeliveryAsync(string key, TenantConfiguration tenant, CancellationToken cancellationToken)
    {
        var delivery = new BusOutboxDeliveryService<ExternalApplicationsContext>(
            provider.GetRequiredService<IBusControl>(),
            provider.GetRequiredService<IOptions<OutboxDeliveryServiceOptions>>(),
            provider.GetRequiredService<IOptions<EntityFrameworkOutboxOptions<ExternalApplicationsContext>>>(),
            notificationHub.GetOrAdd(key),
            provider.GetRequiredService<ILogger<BusOutboxDeliveryService<ExternalApplicationsContext>>>(),
            new TenantScopedServiceProvider(provider, tenant));

        await delivery.StartAsync(cancellationToken);
        _running[key] = new RunningDelivery(tenant.Name, delivery);

        logger.LogInformation("Started outbox delivery for tenant {TenantName}", tenant.Name);
    }

    private async Task StopDeliveryAsync(string key)
    {
        if (!_running.Remove(key, out var running))
            return;

        try
        {
            await running.Delivery.StopAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error stopping outbox delivery for tenant {TenantName}", running.TenantName);
        }
        finally
        {
            running.Delivery.Dispose();
            notificationHub.Remove(key);
        }

        logger.LogInformation("Stopped outbox delivery for tenant {TenantName}", running.TenantName);
    }

    private sealed record RunningDelivery(string TenantName, BusOutboxDeliveryService<ExternalApplicationsContext> Delivery);
}
