using GovUK.Dfe.FlexForms.Domain.Models.EventMapping;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Provides event field mapping configurations for the current tenant.
/// </summary>
public interface IEventMappingProvider
{
    /// <summary>
    /// Gets the event mapping configuration for a specific event type and template.
    /// </summary>
    /// <param name="templateId">The form template ID (API GUID or schema-embedded id such as form-001)</param>
    /// <param name="eventType">The event type name (e.g. "TransferApplicationSubmittedEvent")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The mapping configuration, or null if not found</returns>
    Task<EventFieldMapping?> GetMappingAsync(
        string templateId,
        string eventType,
        CancellationToken cancellationToken = default);
}
