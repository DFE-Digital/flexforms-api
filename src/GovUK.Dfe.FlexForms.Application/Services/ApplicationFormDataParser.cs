using System.Text.Json;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Turns a stored <c>ApplicationResponse.ResponseBody</c> JSON document into the flat
/// fieldId -> value dictionary that <see cref="IEventDataMapper"/> expects.
/// </summary>
public static class ApplicationFormDataParser
{
    /// <summary>
    /// Property names that may wrap the field dictionary in older response payloads.
    /// </summary>
    private static readonly string[] WrapperPropertyNames = ["formData", "FormData", "data", "Data"];

    /// <summary>
    /// Parses the response body. Returns an empty dictionary when the body is missing or not an object.
    /// Values are kept as <see cref="JsonElement"/> so nested objects, arrays and JSON-in-string
    /// fields all round-trip to the mapper unchanged.
    /// </summary>
    public static Dictionary<string, object> Parse(string? responseBody)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(responseBody))
            return result;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(responseBody);
        }
        catch (JsonException)
        {
            return result;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return result;

            foreach (var wrapper in WrapperPropertyNames)
            {
                if (root.TryGetProperty(wrapper, out var wrapped) && wrapped.ValueKind == JsonValueKind.Object)
                {
                    root = wrapped;
                    break;
                }
            }

            foreach (var property in root.EnumerateObject())
            {
                if (property.Name.StartsWith("TaskStatus_", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (property.Value.ValueKind == JsonValueKind.Null)
                    continue;

                var fieldValue = UnwrapStoredFieldValue(property.Value);

                // Clone so values stay usable after the JsonDocument is disposed.
                result[property.Name] = fieldValue.Clone();
            }
        }

        return result;
    }

    /// <summary>
    /// Stored responses wrap each field in metadata ({ question, value, completed, dataType }).
    /// Event mapping and email personalisation need the inner <c>value</c>.
    /// </summary>
    private static JsonElement UnwrapStoredFieldValue(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("value", out var wrapped)
            && wrapped.ValueKind != JsonValueKind.Null)
        {
            return wrapped;
        }

        return element;
    }
}
