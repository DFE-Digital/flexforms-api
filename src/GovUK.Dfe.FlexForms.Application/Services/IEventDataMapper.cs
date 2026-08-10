namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Maps form data to event models based on tenant-configured field mappings.
/// </summary>
public interface IEventDataMapper
{
    /// <summary>
    /// Maps accumulated form data to a specific event type using the configured mapping.
    /// </summary>
    /// <param name="platformMetadata">
    /// Optional platform context for <c>sourceType: Metadata</c> keys
    /// (see <see cref="Options.PlatformEventMetadataKeys"/>).
    /// </param>
    Task<TEvent> MapToEventAsync<TEvent>(
        Dictionary<string, object> formData,
        string templateId,
        string mappingId,
        Guid applicationId,
        string applicationReference,
        IReadOnlyDictionary<string, object?>? platformMetadata = null,
        CancellationToken cancellationToken = default) where TEvent : class;

    /// <summary>
    /// Maps form data to a dictionary payload using the mapping for <paramref name="eventTypeName"/>
    /// (used for schema events that have no CLR contract).
    /// </summary>
    /// <param name="platformMetadata">
    /// Optional platform context for <c>sourceType: Metadata</c> keys
    /// (see <see cref="Options.PlatformEventMetadataKeys"/>).
    /// </param>
    Task<Dictionary<string, object?>> MapToDictionaryAsync(
        Dictionary<string, object> formData,
        string templateId,
        string eventTypeName,
        string mappingId,
        Guid applicationId,
        string applicationReference,
        IReadOnlyDictionary<string, object?>? platformMetadata = null,
        CancellationToken cancellationToken = default);
}
