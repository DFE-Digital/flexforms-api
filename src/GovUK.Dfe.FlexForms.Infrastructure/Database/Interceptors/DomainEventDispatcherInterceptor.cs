using GovUK.Dfe.FlexForms.Domain.Common;
using MassTransit.EntityFrameworkCoreIntegration;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Infrastructure.Database.Interceptors;

/// <summary>
/// Collects domain events before save and publishes them after a successful commit.
/// Publishing during <see cref="SavingChangesAsync"/> caused nested queries on the same
/// DbContext (e.g. file-validation notifications looking up the uploader) and
/// silently dropped the GOV.UK banner.
/// </summary>
/// <remarks>
/// With the MassTransit bus outbox enabled, integration events published by those handlers are
/// only added to the change tracker. They are flushed here with a follow-up save, otherwise they
/// would never reach the outbox tables.
/// </remarks>
public class DomainEventDispatcherInterceptor(IMediator mediator, ILogger? logger = null) : SaveChangesInterceptor
{
    private List<IDomainEvent>? _pending;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        CollectPendingEvents(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await PublishPendingAsync(cancellationToken);
        await FlushOutboxAsync(eventData.Context, cancellationToken);
        return result;
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        _pending = null;
        return Task.CompletedTask;
    }

    private void CollectPendingEvents(DbContext? context)
    {
        _pending = null;
        if (context is null)
            return;

        var entitiesWithEvents = context.ChangeTracker
            .Entries<IHasDomainEvents>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Any())
            .ToList();

        if (entitiesWithEvents.Count == 0)
            return;

        _pending = entitiesWithEvents
            .SelectMany(e => e.DomainEvents)
            .ToList();

        entitiesWithEvents.ForEach(e => e.ClearDomainEvents());
    }

    private async Task PublishPendingAsync(CancellationToken cancellationToken)
    {
        var events = _pending;
        _pending = null;
        if (events is null || events.Count == 0)
            return;

        foreach (var @event in events)
            await mediator.Publish(@event, cancellationToken);
    }

    private async Task FlushOutboxAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is null)
            return;

        var hasPendingOutboxMessages = context.ChangeTracker
            .Entries<OutboxMessage>()
            .Any(e => e.State == EntityState.Added);

        if (!hasPendingOutboxMessages)
            return;

        // The business change is already committed; a failed flush must not fail the request.
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to persist integration events to the transactional outbox after commit.");
        }
    }
}
