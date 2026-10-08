using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Events;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Utils.Configuration;
using MassTransit;
using MassTransit.DependencyInjection;
using MassTransit.Middleware;
using Microsoft.Extensions.Options;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Chooses, per event, between the scoped (outbox-backed) endpoints and a direct bus publish.
/// </summary>
public interface IMessageEndpointSelector
{
    IPublishEndpoint GetPublishEndpoint(Type messageType);

    ISendEndpointProvider GetSendEndpointProvider(string eventType, string? topicName);
}

/// <inheritdoc />
/// <remarks>
/// The scoped endpoints are only bypassed when they are actually outbox-backed. Inside a consumer
/// they wrap the consume context, and keeping them preserves correlation/conversation ids.
/// Routing uses the host <see cref="OutboxOptions"/> overlaid with the current tenant's
/// <c>MassTransit:Outbox</c> settings (see <see cref="TenantOutboxRouting"/>).
/// </remarks>
public sealed class MessageEndpointSelector(
    IPublishEndpoint scopedPublishEndpoint,
    ISendEndpointProvider scopedSendEndpointProvider,
    IBus bus,
    IScopedBusContextProvider<IBus> scopedBusContextProvider,
    IOptions<OutboxOptions> options,
    ITenantContextAccessor? tenantContextAccessor = null) : IMessageEndpointSelector
{
    public IPublishEndpoint GetPublishEndpoint(Type messageType)
        => UseScopedEndpoints(messageType.Name, messageType.FullName) ? scopedPublishEndpoint : bus;

    public ISendEndpointProvider GetSendEndpointProvider(string eventType, string? topicName)
        => UseScopedEndpoints(eventType, topicName) ? scopedSendEndpointProvider : bus;

    /// <summary>
    /// Events whose consumers rely on the outbox's atomicity and are idempotent, so they always use
    /// the scoped (outbox) endpoints regardless of <see cref="OutboxOptions"/>.
    /// </summary>
    private static readonly HashSet<string> AlwaysOutboxEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(ApplicationProjectionRequestedEvent),
        typeof(ApplicationProjectionRequestedEvent).FullName!,
        nameof(TemplateVersionPublishedEvent),
        typeof(TemplateVersionPublishedEvent).FullName!
    };

    private bool UseScopedEndpoints(params string?[] identifiers)
        => identifiers.Any(id => id is not null && AlwaysOutboxEvents.Contains(id))
           || TenantOutboxRouting.Resolve(options.Value, tenantContextAccessor?.CurrentTenant).UsesOutbox(identifiers)
           || scopedBusContextProvider.Context is not OutboxSendContext;
}
