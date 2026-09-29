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
/// </remarks>
public sealed class MessageEndpointSelector(
    IPublishEndpoint scopedPublishEndpoint,
    ISendEndpointProvider scopedSendEndpointProvider,
    IBus bus,
    IScopedBusContextProvider<IBus> scopedBusContextProvider,
    IOptions<OutboxOptions> options) : IMessageEndpointSelector
{
    public IPublishEndpoint GetPublishEndpoint(Type messageType)
        => UseScopedEndpoints(messageType.Name, messageType.FullName) ? scopedPublishEndpoint : bus;

    public ISendEndpointProvider GetSendEndpointProvider(string eventType, string? topicName)
        => UseScopedEndpoints(eventType, topicName) ? scopedSendEndpointProvider : bus;

    private bool UseScopedEndpoints(params string?[] identifiers)
        => options.Value.UsesOutbox(identifiers)
           || scopedBusContextProvider.Context is not OutboxSendContext;
}
