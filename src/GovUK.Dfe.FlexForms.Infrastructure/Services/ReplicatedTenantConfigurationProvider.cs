using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Utils.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace GovUK.Dfe.FlexForms.Infrastructure.Services;

/// <summary>
/// Wraps the database tenant configuration provider so an admin-triggered refresh reaches every API replica.
/// <see cref="RefreshAsync"/> reloads this replica and then publishes on Redis; as a hosted service it reloads
/// this replica when another one publishes. The inner provider's periodic refresh is not broadcast and remains
/// the fallback for replicas that miss a message (Redis pub/sub does not queue).
/// </summary>
public sealed class ReplicatedTenantConfigurationProvider(
    ITenantConfigurationProvider inner,
    IServiceProvider serviceProvider,
    ILogger<ReplicatedTenantConfigurationProvider> logger) : BackgroundService, ITenantConfigurationProvider
{
    internal static readonly RedisChannel Channel =
        RedisChannel.Literal(FlexFormsCacheKeys.ApiTenantConfigurationChangedChannel);

    private readonly string _instanceId = Guid.NewGuid().ToString("N");

    internal TimeSpan SubscribeRetryDelay { get; init; } = TimeSpan.FromSeconds(30);

    public string Source => inner.Source;

    public TenantConfiguration? GetTenant(Guid id) => inner.GetTenant(id);

    public TenantConfiguration? GetTenantByOrigin(string origin) => inner.GetTenantByOrigin(origin);

    public IReadOnlyCollection<TenantConfiguration> GetAllTenants() => inner.GetAllTenants();

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await inner.RefreshAsync(cancellationToken);

        try
        {
            await GetRedis().GetSubscriber().PublishAsync(Channel, _instanceId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not tell other API replicas to reload tenant configuration; they pick it up on their next periodic refresh");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await GetRedis().GetSubscriber().SubscribeAsync(Channel, (_, message) => OnMessage(message));
                logger.LogInformation("Listening for tenant configuration changes from other API replicas");
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    ex,
                    "Could not subscribe to tenant configuration changes; retrying in {RetryDelay}",
                    SubscribeRetryDelay);
            }

            try
            {
                await Task.Delay(SubscribeRetryDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    internal void OnMessage(RedisValue message)
    {
        if (message.ToString() == _instanceId)
        {
            return;
        }

        logger.LogInformation("Reloading tenant configuration after a change on another API replica");
        _ = ReloadFromPeerAsync();
    }

    private async Task ReloadFromPeerAsync()
    {
        try
        {
            await inner.RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reload tenant configuration after a change on another API replica");
        }
    }

    // Resolved per call: the CoreLibs registration connects on first resolve and throws while Redis is unreachable.
    private IConnectionMultiplexer GetRedis() => serviceProvider.GetRequiredService<IConnectionMultiplexer>();
}
