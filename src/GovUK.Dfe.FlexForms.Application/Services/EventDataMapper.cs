using System.Text.Json;
using GovUK.Dfe.FlexForms.Domain.Models.EventMapping;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Maps accumulated form data onto event payloads using the tenant's configured field mappings.
/// </summary>
public sealed class EventDataMapper(
    IEventMappingProvider mappingProvider,
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
                var value = ExtractValue(
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

    /// <summary>
    /// Extracts a value based on the field mapping configuration.
    /// </summary>
    private object ExtractValue(
        FieldMapping fieldMapping,
        Dictionary<string, object> formData,
        Guid applicationId,
        string applicationReference,
        IReadOnlyDictionary<string, object?>? platformMetadata)
    {
        return fieldMapping.SourceType switch
        {
            FieldMappingSourceType.DirectField =>
                GetFieldValue(fieldMapping.SourceFieldId!, formData),

            FieldMappingSourceType.ComplexFieldProperty =>
                GetComplexFieldProperty(fieldMapping.SourceFieldId!, fieldMapping.NestedPath!, formData),

            FieldMappingSourceType.Collection =>
                GetCollectionValues(fieldMapping.CollectionMapping!, formData, platformMetadata),

            FieldMappingSourceType.Computed =>
                ComputeValue(
                    fieldMapping.SourceFieldIds!,
                    formData,
                    fieldMapping.TransformationType!,
                    fieldMapping.TransformationConfig,
                    fieldMapping.DefaultValue),

            FieldMappingSourceType.Static =>
                ResolveStaticValue(fieldMapping.TransformationType, fieldMapping.DefaultValue),

            FieldMappingSourceType.Metadata =>
                GetMetadataValue(
                    fieldMapping.SourceFieldId!,
                    applicationId,
                    applicationReference,
                    platformMetadata,
                    fieldMapping.DefaultValue),

            _ => fieldMapping.DefaultValue ?? string.Empty
        };
    }

    /// <summary>
    /// Gets a field value directly from form data.
    /// </summary>
    private object GetFieldValue(string fieldId, Dictionary<string, object> formData)
    {
        if (!formData.TryGetValue(fieldId, out var value))
        {
            logger.LogDebug("Field {FieldId} not found in form data", fieldId);
            return string.Empty;
        }

        var cleaned = CleanValue(value);
        return cleaned ?? string.Empty;
    }

    /// <summary>
    /// Unwraps a <see cref="JsonElement"/> into its CLR scalar equivalent.
    /// </summary>
    private static object? CleanValue(object? value)
    {
        if (value == null)
            return null;

        if (value is JsonElement jsonElement)
        {
            return jsonElement.ValueKind switch
            {
                JsonValueKind.String => jsonElement.GetString(),
                JsonValueKind.Number => jsonElement.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => jsonElement.ToString()
            };
        }

        return value;
    }

    /// <summary>
    /// Gets a property from a complex field (e.g., autocomplete field with nested data).
    /// </summary>
    private object GetComplexFieldProperty(string fieldId, string propertyPath, Dictionary<string, object> formData)
    {
        if (!formData.TryGetValue(fieldId, out var fieldValue))
        {
            return string.Empty;
        }

        try
        {
            var valueStr = fieldValue?.ToString();
            if (string.IsNullOrEmpty(valueStr))
            {
                return string.Empty;
            }

            // Decode HTML entities and parse JSON
            var decoded = System.Net.WebUtility.HtmlDecode(valueStr);
            var complexData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(decoded);

            if (complexData?.TryGetValue(propertyPath, out var propertyValue) == true)
            {
                if (propertyValue.ValueKind == JsonValueKind.String)
                {
                    var result = propertyValue.GetString() ?? string.Empty;
                    logger.LogDebug("Extracted string value: {Value}", result);
                    return result;
                }

                if (propertyValue.ValueKind == JsonValueKind.Object)
                {
                    // Nested objects (e.g. gor: { name: "...", code: "..." }) prefer their display name.
                    if (propertyValue.TryGetProperty("name", out var nameProperty)
                        && nameProperty.ValueKind == JsonValueKind.String)
                    {
                        var result = nameProperty.GetString() ?? string.Empty;
                        logger.LogDebug("Extracted nested name value: {Value}", result);
                        return result;
                    }

                    logger.LogDebug("Nested object has no 'name' property, returning JSON");
                    return propertyValue.ToString();
                }

                return propertyValue.ToString();
            }

            logger.LogDebug(
                "Property {PropertyPath} not found in complex field {FieldId}",
                propertyPath,
                fieldId);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                ex,
                "Failed to parse complex field {FieldId}.{PropertyPath}",
                fieldId,
                propertyPath);
        }

        return string.Empty;
    }

    /// <summary>
    /// Gets values from a collection flow (multi-collection or derived).
    /// </summary>
    private object GetCollectionValues(
        CollectionMapping collectionMapping,
        Dictionary<string, object> formData,
        IReadOnlyDictionary<string, object?>? platformMetadata)
    {
        if (!formData.TryGetValue(collectionMapping.SourceCollectionFieldId, out var collectionValue))
        {
            logger.LogDebug(
                "Collection {CollectionId} not found in form data",
                collectionMapping.SourceCollectionFieldId);
            return collectionMapping.ItemMappings != null ? new List<object>() : string.Empty;
        }

        try
        {
            var decoded = System.Net.WebUtility.HtmlDecode(collectionValue?.ToString());
            if (string.IsNullOrEmpty(decoded))
            {
                return collectionMapping.ItemMappings != null ? new List<object>() : string.Empty;
            }

            var items = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(decoded);
            if (items == null || items.Count == 0)
            {
                return collectionMapping.ItemMappings != null ? new List<object>() : string.Empty;
            }

            logger.LogDebug(
                "Found {Count} items in collection {CollectionId}",
                items.Count,
                collectionMapping.SourceCollectionFieldId);

            // Extract single value from first item
            if (collectionMapping.ExtractFirst && !string.IsNullOrEmpty(collectionMapping.NestedPath))
            {
                var firstItem = items.First();
                return ExtractNestedProperty(firstItem, collectionMapping.NestedPath);
            }

            // Map all items to event structure
            if (collectionMapping.ItemMappings != null)
            {
                var mappedItems = new List<Dictionary<string, object>>();

                foreach (var item in items)
                {
                    var mappedItem = new Dictionary<string, object>();
                    var itemData = ConvertToFormData(item);

                    // Merge with original formData for cross-collection references
                    var mergedData = new Dictionary<string, object>(formData);
                    foreach (var kvp in itemData)
                    {
                        mergedData[kvp.Key] = kvp.Value;
                    }

                    foreach (var (propertyName, itemMapping) in collectionMapping.ItemMappings)
                    {
                        var value = ExtractValue(
                            itemMapping,
                            mergedData,
                            Guid.Empty,
                            string.Empty,
                            platformMetadata);

                        // Omit empty optional properties so the deserializer can apply defaults.
                        if (value == null || (value is string str && string.IsNullOrEmpty(str)))
                            continue;

                        mappedItem[propertyName] = value;
                    }

                    mappedItems.Add(mappedItem);
                }

                logger.LogDebug(
                    "Mapped {Count} items from collection {CollectionId}",
                    mappedItems.Count,
                    collectionMapping.SourceCollectionFieldId);

                return mappedItems;
            }

            return items;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                ex,
                "Failed to parse collection {CollectionId}",
                collectionMapping.SourceCollectionFieldId);
            return new List<object>();
        }
    }

    /// <summary>
    /// Converts a JsonElement dictionary to an object dictionary for form data processing.
    /// </summary>
    private static Dictionary<string, object> ConvertToFormData(Dictionary<string, JsonElement> item)
    {
        var result = new Dictionary<string, object>();

        foreach (var (key, value) in item)
        {
            if (key.Equals("id", StringComparison.OrdinalIgnoreCase))
                continue;

            result[key] = value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : value.ToString();
        }

        return result;
    }

    /// <summary>
    /// Extracts a nested property from a dictionary using dot notation.
    /// </summary>
    private object ExtractNestedProperty(Dictionary<string, JsonElement> source, string path)
    {
        var parts = path.Split('.');
        JsonElement current = default;
        var found = false;

        foreach (var part in parts)
        {
            if (!found && source.TryGetValue(part, out current))
            {
                found = true;
            }
            else if (found && current.ValueKind == JsonValueKind.Object && current.TryGetProperty(part, out current))
            {
                continue;
            }
            else if (found && current.ValueKind == JsonValueKind.String)
            {
                var decoded = System.Net.WebUtility.HtmlDecode(current.GetString());
                try
                {
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(decoded!);
                    if (parsed?.TryGetValue(part, out current) == true)
                    {
                        continue;
                    }
                }
                catch (JsonException)
                {
                    logger.LogDebug("Failed to parse nested JSON at path {Path}", path);
                }

                return string.Empty;
            }
            else
            {
                logger.LogDebug("Path {Path} not found in source data", path);
                return string.Empty;
            }
        }

        return found && current.ValueKind == JsonValueKind.String
            ? current.GetString() ?? string.Empty
            : current.ToString();
    }

    /// <summary>
    /// Computes a value from multiple fields using a transformation.
    /// </summary>
    private object ComputeValue(
        List<string> sourceFieldIds,
        Dictionary<string, object> formData,
        string transformationType,
        Dictionary<string, object>? config,
        object? defaultValue)
    {
        var values = sourceFieldIds
            .Select(id => GetFieldValue(id, formData))
            .Where(v => v != null && !string.IsNullOrEmpty(v.ToString()))
            .ToList();

        logger.LogDebug(
            "Computing value using {TransformationType} from {Count} source fields",
            transformationType,
            values.Count);

        return transformationType switch
        {
            "checkEquals" => values.FirstOrDefault()?.ToString() == config?["compareValue"]?.ToString(),
            "concatenate" => string.Join(" ", values),
            "sum" => values.Sum(v => Convert.ToDouble(v)),
            "count" => values.Count,
            "any" => values.Any(),
            _ => defaultValue ?? string.Empty
        };
    }

    /// <summary>
    /// Resolves a static or generated value.
    /// </summary>
    private static object ResolveStaticValue(string? transformationType, object? defaultValue)
    {
        return transformationType switch
        {
            "currentDateTime" => DateTime.UtcNow,
            "currentDate" => DateTime.UtcNow.Date,
            _ => defaultValue ?? string.Empty
        };
    }

    /// <summary>
    /// Resolves platform Metadata keys. Prefers the trigger-supplied context bag, then
    /// falls back to application identity always available on every dispatch.
    /// </summary>
    private static object GetMetadataValue(
        string metadataKey,
        Guid applicationId,
        string applicationReference,
        IReadOnlyDictionary<string, object?>? platformMetadata,
        object? defaultValue)
    {
        if (platformMetadata is not null
            && TryGetIgnoreCase(platformMetadata, metadataKey, out var fromContext)
            && fromContext is not null
            && !(fromContext is string s && string.IsNullOrEmpty(s)))
        {
            return fromContext;
        }

        return metadataKey.ToLowerInvariant() switch
        {
            "applicationid" => applicationId.ToString(),
            "applicationreference" => applicationReference,
            _ => defaultValue ?? string.Empty
        };
    }

    private static bool TryGetIgnoreCase(
        IReadOnlyDictionary<string, object?> source,
        string key,
        out object? value)
    {
        if (source.TryGetValue(key, out value))
            return true;

        foreach (var kvp in source)
        {
            if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = kvp.Value;
                return true;
            }
        }

        value = null;
        return false;
    }
}
