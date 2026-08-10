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
                if (property.Value.ValueKind == JsonValueKind.Null)
                    continue;

                // Clone so values stay usable after the JsonDocument is disposed.
                result[property.Name] = property.Value.Clone();
            }
        }

        return result;
    }
}
