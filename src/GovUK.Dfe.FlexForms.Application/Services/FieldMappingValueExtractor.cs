using System.Text.Json;
using GovUK.Dfe.FlexForms.Domain.Models.EventMapping;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Extracts values from form data and platform metadata using the shared field-mapping DSL.
/// </summary>
public sealed class FieldMappingValueExtractor(
    ILogger<FieldMappingValueExtractor> logger) : IFieldMappingValueExtractor
{
    /// <inheritdoc />
    public object? ExtractValue(
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

            if (collectionMapping.ExtractFirst && !string.IsNullOrEmpty(collectionMapping.NestedPath))
            {
                var firstItem = items.First();
                return ExtractNestedProperty(firstItem, collectionMapping.NestedPath);
            }

            if (collectionMapping.ItemMappings != null)
            {
                var mappedItems = new List<Dictionary<string, object>>();

                foreach (var item in items)
                {
                    var mappedItem = new Dictionary<string, object>();
                    var itemData = ConvertToFormData(item);

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

    private static object ResolveStaticValue(string? transformationType, object? defaultValue)
    {
        return transformationType switch
        {
            "currentDateTime" => DateTime.UtcNow,
            "currentDate" => DateTime.UtcNow.Date,
            _ => defaultValue ?? string.Empty
        };
    }

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
