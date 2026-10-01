using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.CoreLibs.Messaging.MassTransit.Interfaces;
using GovUK.Dfe.CoreLibs.Messaging.MassTransit.Models;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// <see cref="IEventPublisher"/> that publishes to the shared platform Service Bus
/// (one namespace, one bus host) and stamps every outbound message with the current
/// tenant's id and name as headers. Consumers resolve tenant context from those headers
/// via <c>TenantContextConsumeFilter</c>. Whether a message goes through the transactional outbox
/// is decided per message type by <see cref="IMessageEndpointSelector"/>.
/// </summary>
public sealed class TenantAwareEventPublisher(
    IMessageEndpointSelector endpointSelector,
    ITenantContextAccessor tenantAccessor,
    ILogger<TenantAwareEventPublisher> logger) : IEventPublisher
{
    private const string TenantIdHeader = "TenantId";
    private const string TenantNameHeader = "TenantName";

    public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default) where T : class
        => PublishAsync(@event, properties: null, cancellationToken);

    public Task PublishAsync<T>(
        T @event,
        AzureServiceBusMessageProperties? properties,
        CancellationToken cancellationToken = default) where T : class
    {
        var tenant = tenantAccessor.CurrentTenant
            ?? throw new InvalidOperationException("Tenant context is required to publish events.");

        logger.LogDebug(
            "Publishing {MessageType} for tenant {TenantId} ({TenantName})",
            typeof(T).Name, tenant.Id, tenant.Name);

        return endpointSelector.GetPublishEndpoint(typeof(T)).Publish(@event, ctx =>
        {
            ctx.Headers.Set(TenantIdHeader, tenant.Id.ToString());
            ctx.Headers.Set(TenantNameHeader, tenant.Name);

            if (properties is not null)
            {
                ApplyMessageProperties(ctx, properties);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Mirrors <c>MassTransitEventPublisher.ApplyMessageProperties</c> behaviour.
    /// Inlined because the original is internal to the CoreLibs assembly.
    /// </summary>
    private static void ApplyMessageProperties(PublishContext context, AzureServiceBusMessageProperties properties)
    {
        if (!string.IsNullOrWhiteSpace(properties.ContentType))
            context.ContentType = new System.Net.Mime.ContentType(properties.ContentType);

        if (!string.IsNullOrWhiteSpace(properties.CorrelationId))
            context.CorrelationId = Guid.Parse(properties.CorrelationId);

        if (!string.IsNullOrWhiteSpace(properties.MessageId))
            context.MessageId = Guid.Parse(properties.MessageId);

        if (properties.TimeToLive.HasValue)
            context.TimeToLive = properties.TimeToLive.Value;

        if (!string.IsNullOrWhiteSpace(properties.PartitionKey))
            context.Headers.Set("PartitionKey", properties.PartitionKey);

        if (!string.IsNullOrWhiteSpace(properties.SessionId))
            context.Headers.Set("SessionId", properties.SessionId);

        if (!string.IsNullOrWhiteSpace(properties.ReplyTo))
            context.Headers.Set("ReplyTo", properties.ReplyTo);

        if (!string.IsNullOrWhiteSpace(properties.ReplyToSessionId))
            context.Headers.Set("ReplyToSessionId", properties.ReplyToSessionId);

        if (properties.ScheduledEnqueueTime.HasValue)
            context.Headers.Set("ScheduledEnqueueTimeUtc", properties.ScheduledEnqueueTime.Value);

        if (!string.IsNullOrWhiteSpace(properties.Subject))
            context.Headers.Set("Label", properties.Subject);

        if (!string.IsNullOrWhiteSpace(properties.To))
            context.Headers.Set("To", properties.To);

        foreach (var customProperty in properties.CustomProperties)
        {
            context.Headers.Set(customProperty.Key, customProperty.Value);
        }
    }
}
