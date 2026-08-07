using System.Reflection;
using GovUK.Dfe.CoreLibs.Messaging.MassTransit.Interfaces;
using GovUK.Dfe.CoreLibs.Messaging.MassTransit.Models;
using GovUK.Dfe.FlexForms.Application.Options;
using GovUK.Dfe.FlexForms.Domain.Models.Messaging;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Publishes the events a tenant has bound to a lifecycle trigger via the
/// <c>EventTriggers</c> TenantConfig category.
/// </summary>
/// <remarks>
/// Virus scanning is deliberately not configurable here: <c>ScanRequestedEvent</c> is published
/// directly by the file upload handler so administrators cannot disable it.
/// </remarks>
public sealed class EventTriggerDispatcher(
    ITenantContextAccessor tenantContextAccessor,
    IEventDataMapper eventDataMapper,
    IEventPublisher eventPublisher,
    ISendEndpointProvider sendEndpointProvider,
    IEventTypeRegistry eventTypeRegistry,
    ISchemaEventDefinitionProvider schemaEventDefinitionProvider,
    ILogger<EventTriggerDispatcher> logger) : IEventTriggerDispatcher
{
    /// <summary>Event that is always published by the platform and can never be configured away.</summary>
    private const string SystemOnlyEventType = "ScanRequestedEvent";

    /// <summary>Pre-EventTriggers location for submit-time publishing; still honoured for migration.</summary>
    private const string LegacySubmitSection = "ApplicationSubmission:PublishEvent";

    private static readonly MethodInfo MapToEventAsyncMethod =
        typeof(IEventDataMapper).GetMethod(nameof(IEventDataMapper.MapToEventAsync))!;

    /// <inheritdoc />
    public async Task DispatchAsync(
        string triggerName,
        Guid applicationId,
        string applicationReference,
        string templateId,
        Dictionary<string, object> formData,
        IReadOnlyDictionary<string, object?>? platformMetadata = null,
        CancellationToken cancellationToken = default)
    {
        var tenant = tenantContextAccessor.CurrentTenant;
        if (tenant is null)
        {
            logger.LogWarning(
                "No tenant context available; skipping {Trigger} event dispatch for application {ApplicationId}.",
                triggerName,
                applicationId);
            return;
        }

        var entries = ResolveEntries(tenant, triggerName);
        if (entries.Count == 0)
        {
            logger.LogDebug(
                "No EventTriggers configured for {Trigger} in tenant {TenantName}.",
                triggerName,
                tenant.Name);
            return;
        }

        var serviceName = $"extapi-{tenant.Name}";

        // Ensure application identity is always present for Metadata mappings.
        var metadata = MergeApplicationIdentity(platformMetadata, applicationId, applicationReference);

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.EventType) || string.IsNullOrWhiteSpace(entry.MappingId))
            {
                logger.LogWarning(
                    "Skipping {Trigger} event entry with missing EventType or MappingId.",
                    triggerName);
                continue;
            }

            if (string.Equals(entry.EventType, SystemOnlyEventType, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "{EventType} is published by the platform and cannot be configured via EventTriggers. Skipping.",
                    entry.EventType);
                continue;
            }

            try
            {
                var kind = string.IsNullOrWhiteSpace(entry.EventKind)
                    ? EventPublishKind.Typed
                    : entry.EventKind.Trim();

                if (string.Equals(kind, EventPublishKind.Schema, StringComparison.OrdinalIgnoreCase))
                {
                    await PublishSchemaEventAsync(
                        entry,
                        tenant,
                        serviceName,
                        applicationId,
                        applicationReference,
                        templateId,
                        formData,
                        metadata,
                        cancellationToken);
                }
                else
                {
                    await PublishTypedEventAsync(
                        entry,
                        serviceName,
                        applicationId,
                        applicationReference,
                        templateId,
                        formData,
                        metadata,
                        cancellationToken);
                }
            }
            catch (Exception ex)
            {
                // Publishing is best-effort: the submit/upload that raised the trigger has already succeeded.
                logger.LogError(
                    ex,
                    "Failed to publish {EventType} for {Trigger} on application {ApplicationId}; continuing with next event.",
                    entry.EventType,
                    triggerName,
                    applicationId);
            }
        }
    }

    private static IReadOnlyDictionary<string, object?> MergeApplicationIdentity(
        IReadOnlyDictionary<string, object?>? platformMetadata,
        Guid applicationId,
        string applicationReference)
    {
        var merged = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (platformMetadata is not null)
        {
            foreach (var kvp in platformMetadata)
                merged[kvp.Key] = kvp.Value;
        }

        merged.TryAdd(PlatformEventMetadataKeys.ApplicationId, applicationId.ToString());
        merged.TryAdd(PlatformEventMetadataKeys.ApplicationReference, applicationReference);
        return merged;
    }

    /// <summary>
    /// Reads <c>EventTriggers:{trigger}</c>, falling back to the legacy submit-time
    /// <c>ApplicationSubmission:PublishEvent:Events</c> list.
    /// </summary>
    private List<EventEntryOptions> ResolveEntries(TenantConfiguration tenant, string triggerName)
    {
        var entries = BindEntries(
            tenant.Settings.GetSection($"{EventTriggersOptions.SectionName}:{triggerName}"));

        if (entries.Count > 0
            || !string.Equals(triggerName, EventTriggerType.ApplicationSubmitted, StringComparison.OrdinalIgnoreCase))
        {
            return entries;
        }

        var legacy = tenant.Settings.GetSection(LegacySubmitSection);
        if (!legacy.Exists())
            return entries;

        var enabled = legacy.GetValue<bool?>("Enabled") ?? true;
        if (!enabled)
            return entries;

        var legacyEntries = BindEntries(legacy.GetSection("Events"));
        if (legacyEntries.Count > 0)
        {
            logger.LogInformation(
                "Using legacy {Section}:Events for {Trigger} in tenant {TenantName}. Migrate these to the EventTriggers category.",
                LegacySubmitSection,
                triggerName,
                tenant.Name);
        }

        return legacyEntries;
    }

    private static List<EventEntryOptions> BindEntries(IConfigurationSection section)
    {
        var entries = new List<EventEntryOptions>();
        if (!section.Exists())
            return entries;

        foreach (var child in section.GetChildren())
        {
            var entry = new EventEntryOptions();
            child.Bind(entry);

            if (string.IsNullOrWhiteSpace(entry.EventType))
                entry.EventType = child["eventType"] ?? child["EventType"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(entry.MappingId))
                entry.MappingId = child["mappingId"] ?? child["MappingId"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(entry.EventKind))
                entry.EventKind = child["eventKind"] ?? child["EventKind"] ?? EventPublishKind.Typed;

            entries.Add(entry);
        }

        return entries;
    }

    private async Task PublishTypedEventAsync(
        EventEntryOptions entry,
        string serviceName,
        Guid applicationId,
        string applicationReference,
        string templateId,
        Dictionary<string, object> formData,
        IReadOnlyDictionary<string, object?> platformMetadata,
        CancellationToken cancellationToken)
    {
        var eventType = eventTypeRegistry.GetEventType(entry.EventType);
        if (eventType == null)
        {
            logger.LogWarning("Event type '{EventType}' is not a known platform event. Skipping.", entry.EventType);
            return;
        }

        var eventData = await MapToEventAsync(
            eventDataMapper,
            eventType,
            formData,
            templateId,
            entry.MappingId,
            applicationId,
            applicationReference,
            platformMetadata,
            cancellationToken);

        if (eventData == null)
        {
            logger.LogWarning("Mapping returned null for event type '{EventType}'. Skipping.", entry.EventType);
            return;
        }

        var messageProperties = AzureServiceBusMessagePropertiesBuilder
            .Create()
            .AddCustomProperty("serviceName", serviceName)
            .AddCustomProperty("eventKind", EventPublishKind.Typed)
            .Build();

        await PublishAsync(eventPublisher, eventType, eventData, messageProperties, cancellationToken);

        logger.LogInformation(
            "Published typed {EventType} for application {ApplicationId} with reference {ApplicationReference}",
            entry.EventType,
            applicationId,
            applicationReference);
    }

    private async Task PublishSchemaEventAsync(
        EventEntryOptions entry,
        TenantConfiguration tenant,
        string serviceName,
        Guid applicationId,
        string applicationReference,
        string templateId,
        Dictionary<string, object> formData,
        IReadOnlyDictionary<string, object?> platformMetadata,
        CancellationToken cancellationToken)
    {
        var definition = schemaEventDefinitionProvider.GetDefinition(entry.EventType);
        if (definition is null || string.IsNullOrWhiteSpace(definition.TopicName))
        {
            logger.LogWarning(
                "Schema event '{EventType}' is not defined in SchemaEvents (or topicName is missing). Skipping.",
                entry.EventType);
            return;
        }

        // Same field-mapping DSL as typed events, materialised as a dictionary payload.
        var payload = await eventDataMapper.MapToDictionaryAsync(
            formData,
            templateId,
            entry.EventType,
            entry.MappingId,
            applicationId,
            applicationReference,
            platformMetadata,
            cancellationToken);

        var envelope = new SchemaEventEnvelope
        {
            MessageType = entry.EventType,
            Version = string.IsNullOrWhiteSpace(definition.Version) ? "1.0" : definition.Version,
            TopicName = definition.TopicName,
            Payload = payload,
            Metadata = new Dictionary<string, object?>
            {
                ["applicationId"] = applicationId.ToString(),
                ["applicationReference"] = applicationReference,
                ["templateId"] = templateId
            }
        };

        var endpoint = await sendEndpointProvider.GetSendEndpoint(new Uri($"topic:{definition.TopicName}"));

        await endpoint.Send(envelope, sendContext =>
        {
            sendContext.Headers.Set("MessageType", entry.EventType);
            sendContext.Headers.Set("EventKind", EventPublishKind.Schema);
            sendContext.Headers.Set("serviceName", serviceName);
            sendContext.Headers.Set("TenantId", tenant.Id.ToString());
            sendContext.Headers.Set("TenantName", tenant.Name);
            if (!string.IsNullOrWhiteSpace(definition.Version))
                sendContext.Headers.Set("SchemaVersion", definition.Version);
        }, cancellationToken);

        logger.LogInformation(
            "Published schema event {EventType} to topic {Topic} for application {ApplicationId}",
            entry.EventType,
            definition.TopicName,
            applicationId);
    }

    /// <summary>
    /// Calls <see cref="IEventPublisher.PublishAsync{T}"/> with the concrete event type so MassTransit
    /// routes to the correct topic (publishing as object fails: message types must not be in System).
    /// </summary>
    private static async Task PublishAsync(
        IEventPublisher eventPublisher,
        Type eventType,
        object eventData,
        object messageProperties,
        CancellationToken cancellationToken)
    {
        var publishMethod = typeof(IEventPublisher)
            .GetMethods()
            .First(m => m.Name == nameof(IEventPublisher.PublishAsync)
                        && m.IsGenericMethodDefinition
                        && m.GetParameters().Length == 3);

        var genericPublish = publishMethod.MakeGenericMethod(eventType);
        if (genericPublish.Invoke(eventPublisher, [eventData, messageProperties, cancellationToken]) is Task task)
            await task.ConfigureAwait(false);
    }

    private static async Task<object?> MapToEventAsync(
        IEventDataMapper mapper,
        Type eventType,
        Dictionary<string, object> formData,
        string templateId,
        string mappingId,
        Guid applicationId,
        string applicationReference,
        IReadOnlyDictionary<string, object?> platformMetadata,
        CancellationToken cancellationToken)
    {
        var genericMethod = MapToEventAsyncMethod.MakeGenericMethod(eventType);
        var invoked = genericMethod.Invoke(
            mapper,
            [
                formData,
                templateId,
                mappingId,
                applicationId,
                applicationReference,
                platformMetadata,
                cancellationToken
            ]);

        if (invoked is not Task awaitable)
            return null;

        await awaitable.ConfigureAwait(false);
        return awaitable.GetType().GetProperty("Result")!.GetValue(awaitable);
    }
}
