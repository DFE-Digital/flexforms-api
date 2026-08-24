using System.Text.Json;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin.Validation;

/// <summary>
/// Categories that map host/template GUIDs into the tenant catalogue.
/// Subject to ownership checks on upsert; SuperAdmin edit lock is
/// <see cref="GovUK.Dfe.FlexForms.Domain.Tenancy.SuperAdminOnlyTenantSettingCategories"/>.
/// </summary>
public static class TemplateMappingSettingCategories
{
    public const string ApplicationTemplates = "ApplicationTemplates";
    public const string Template = "Template";

    public static bool IsTemplateMappingCategory(string? category) =>
        !string.IsNullOrWhiteSpace(category)
        && (string.Equals(category.Trim(), ApplicationTemplates, StringComparison.OrdinalIgnoreCase)
            || string.Equals(category.Trim(), Template, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Extracts template GUIDs from ApplicationTemplates / Template JSON
    /// (<c>HostMappings</c> values, <c>TemplateId</c>, <c>Id</c>).
    /// </summary>
    public static IReadOnlyList<Guid> ExtractTemplateIds(string settingsJson)
    {
        var ids = new List<Guid>();
        if (string.IsNullOrWhiteSpace(settingsJson)
            || TenantSettingJsonValidator.IsSecretPlaceholder(settingsJson))
        {
            return ids;
        }

        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return ids;

            CollectFromObject(doc.RootElement, ids);
        }
        catch (JsonException)
        {
            // Structural validation reports JSON errors separately.
        }

        return ids.Distinct().ToList();
    }

    private static void CollectFromObject(JsonElement root, List<Guid> ids)
    {
        if (TryGetProperty(root, "HostMappings", out var mappings)
            && mappings.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in mappings.EnumerateObject())
            {
                TryAddGuid(prop.Value, ids);
            }
        }

        if (TryGetProperty(root, "TemplateId", out var templateId))
            TryAddGuid(templateId, ids);

        if (TryGetProperty(root, "Id", out var id))
            TryAddGuid(id, ids);
    }

    private static void TryAddGuid(JsonElement value, List<Guid> ids)
    {
        var raw = value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                ? null
                : value.ToString();

        if (!string.IsNullOrWhiteSpace(raw)
            && Guid.TryParse(raw, out var guid)
            && guid != Guid.Empty)
        {
            ids.Add(guid);
        }
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        if (root.TryGetProperty(name, out value))
            return true;

        foreach (var prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
