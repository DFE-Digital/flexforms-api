using GovUK.Dfe.FlexForms.Application.Messaging;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Resolves an event type name (from TenantConfig) to a .NET Type for mapping and publishing.
/// </summary>
public interface IEventTypeRegistry
{
    /// <summary>
    /// Gets the .NET type for the given event type name (e.g. "TransferApplicationSubmittedEvent").
    /// </summary>
    Type? GetEventType(string eventTypeName);

    /// <summary>
    /// Returns all discovered platform event types (name, topic, CLR type).
    /// </summary>
    IReadOnlyList<EventCatalogueEntry> GetCatalogue();
}
