using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Maps accumulated form data onto event payloads using the tenant's configured field mappings.
/// </summary>
public sealed class EventDataMapper(
    IEventMappingProvider mappingProvider,
    IFieldMappingValueExtractor valueExtractor,
    ILogger<EventDataMapper> logger) : IEventDataMapper
{
    /// <inheritdoc />
    public async Task<TEvent> MapToEventAsync<TEvent>(
        Dictionary<string, object> formData,
        string templateId,
        string mappingId,
        Guid applicationId,
        string applicationReference,
        IReadOnlyDictionary<string, object?>? platformMetadata = null,
        CancellationToken cancellationToken = default) where TEvent : class
    {
        var eventType = typeof(TEvent).Name;
        var eventData = await BuildEventDataAsync(
            formData,
            templateId,
            eventType,
            mappingId,
            applicationId,
            applicationReference,
            platformMetadata,
            cancellationToken);

        var json = JsonSerializer.Serialize(eventData);
        var eventObject = JsonSerializer.Deserialize<TEvent>(json);

        if (eventObject == null)
        {
            throw new InvalidOperationException($"Failed to deserialize event of type {eventType}");
        }

        logger.LogInformation(
            "Successfully mapped event {EventType} for application {ApplicationId}",
            eventType,
            applicationId);

        return eventObject;
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, object?>> MapToDictionaryAsync(
        Dictionary<string, object> formData,
        string templateId,
        string eventTypeName,
        string mappingId,
        Guid applicationId,
        string applicationReference,
        IReadOnlyDictionary<string, object?>? platformMetadata = null,
        CancellationToken cancellationToken = default)
    {
        var eventData = await BuildEventDataAsync(
            formData,
            templateId,
            eventTypeName,
            mappingId,
            applicationId,
            applicationReference,
            platformMetadata,
            cancellationToken);

        return eventData.ToDictionary(
            kv => kv.Key,
            kv => (object?)kv.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, object>> BuildEventDataAsync(
        Dictionary<string, object> formData,
        string templateId,
        string eventType,
        string mappingId,
        Guid applicationId,
        string applicationReference,
        IReadOnlyDictionary<string, object?>? platformMetadata,
        CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Starting event mapping: {EventType} using mapping {MappingId} for application {ApplicationId}",
            eventType,
            mappingId,
            applicationId);

        var mapping = await mappingProvider.GetMappingAsync(templateId, eventType, cancellationToken);

        if (mapping == null)
        {
            throw new InvalidOperationException(
                $"No mapping found for event type '{eventType}' and template '{templateId}'");
        }

        var eventData = new Dictionary<string, object>();

        foreach (var (propertyName, fieldMapping) in mapping.FieldMappings)
        {
            try
            {
                var value = valueExtractor.ExtractValue(
                    fieldMapping,
                    formData,
                    applicationId,
                    applicationReference,
                    platformMetadata);

                if (value == null || (value is string str && string.IsNullOrEmpty(str)))
                {
                    logger.LogTrace("Skipping property {PropertyName} - null or empty value", propertyName);
                    continue;
                }

                eventData[propertyName] = value;
                logger.LogTrace("Mapped property {PropertyName} = {Value}", propertyName, value);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error extracting value for property {PropertyName} in event {EventType}",
                    propertyName,
                    eventType);
                throw;
            }
        }

        return eventData;
    }
}
