using System.Collections.Concurrent;
using MassTransit;
using MassTransit.Middleware.Outbox;
using Microsoft.Extensions.Options;

namespace GovUK.Dfe.FlexForms.Infrastructure.Messaging.Outbox;

/// <summary>
/// Singleton <see cref="IBusOutboxNotification"/> used by the scoped bus outbox. Each tenant
/// database has its own delivery loop with its own <see cref="BusOutboxNotification"/>, because
/// that type tracks a single waiter. A commit wakes every loop; loops with nothing to deliver
/// return to waiting after one cheap query.
/// </summary>
public sealed class TenantOutboxNotificationHub(IOptions<OutboxDeliveryServiceOptions> options) : IBusOutboxNotification
{
    private readonly ConcurrentDictionary<string, BusOutboxNotification> _notifications = new(StringComparer.OrdinalIgnoreCase);

    public IBusOutboxNotification GetOrAdd(string databaseKey)
        => _notifications.GetOrAdd(databaseKey, _ => new BusOutboxNotification(options));

    public void Remove(string databaseKey) => _notifications.TryRemove(databaseKey, out _);

    public void Delivered()
    {
        foreach (var notification in _notifications.Values)
            notification.Delivered();
    }

    public Task WaitForDelivery(CancellationToken cancellationToken)
        => Task.Delay(options.Value.QueryDelay, cancellationToken);
}
