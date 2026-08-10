using System.Collections.Concurrent;
using GovUK.Dfe.FlexForms.Application.Messaging;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Maps event type names (from TenantConfig) to .NET types by scanning CoreLibs Messaging.Contracts.
/// </summary>
public sealed class EventTypeRegistry : IEventTypeRegistry
{
    private readonly ConcurrentDictionary<string, Type> _eventTypes;
    private readonly IReadOnlyList<EventCatalogueEntry> _catalogue;

    /// <summary>
    /// Creates a registry populated from assembly-scanned messaging contracts.
    /// </summary>
    public EventTypeRegistry(ILogger<EventTypeRegistry>? logger = null)
    {
        var discovered = MessagingEventDiscovery.Discover();
        _eventTypes = new ConcurrentDictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        var catalogue = new List<EventCatalogueEntry>(discovered.Count);

        foreach (var entry in discovered)
        {
            _eventTypes[entry.EventTypeName] = entry.ClrType;
            catalogue.Add(new EventCatalogueEntry(entry.EventTypeName, entry.TopicName, entry.ClrType));

            if (entry.TopicName is null)
            {
                logger?.LogWarning(
                    "Discovered messaging event {EventType} has no matching TopicNames constant; MassTransit topic wiring will be skipped.",
                    entry.EventTypeName);
            }
        }

        _catalogue = catalogue;

        logger?.LogInformation(
            "EventTypeRegistry loaded {Count} event type(s) from Messaging.Contracts.",
            _catalogue.Count);
    }

    /// <inheritdoc />
    public Type? GetEventType(string eventTypeName)
    {
        if (string.IsNullOrEmpty(eventTypeName))
            return null;

        return _eventTypes.TryGetValue(eventTypeName, out var type) ? type : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<EventCatalogueEntry> GetCatalogue() => _catalogue;
}
