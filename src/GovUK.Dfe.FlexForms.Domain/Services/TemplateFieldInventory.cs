using System.Text.Json;

namespace GovUK.Dfe.FlexForms.Domain.Services;

/// <summary>
/// A template field: its collection (empty for top-level fields) and its field ID. Compared case-insensitively,
/// the same way reporting matches fields.
/// </summary>
public sealed record TemplateFieldKey(string ParentFieldId, string FieldId)
{
    public bool Equals(TemplateFieldKey? other) =>
        other is not null
        && string.Equals(ParentFieldId, other.ParentFieldId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(FieldId, other.FieldId, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => HashCode.Combine(
        StringComparer.OrdinalIgnoreCase.GetHashCode(ParentFieldId),
        StringComparer.OrdinalIgnoreCase.GetHashCode(FieldId));

    public override string ToString() => ParentFieldId.Length == 0 ? $"\"{FieldId}\"" : $"\"{FieldId}\" (in collection \"{ParentFieldId}\")";

    /// <summary>The <c>retiredFields</c> entry that retires this field, as the author would write it.</summary>
    public string ToRetiredFieldJson(IReadOnlyCollection<string>? replacedBy = null)
    {
        var parent = ParentFieldId.Length == 0 ? string.Empty : $", \"parentFieldId\": \"{ParentFieldId}\"";
        var replacements = replacedBy is { Count: > 0 } ? $", \"replacedBy\": [{string.Join(", ", replacedBy.Select(id => $"\"{id}\""))}]" : string.Empty;
        return $"{{ \"fieldId\": \"{FieldId}\"{parent}{replacements} }}";
    }
}

/// <summary>A field the template author has declared as retired, optionally naming the fields that replace it.</summary>
public sealed record RetiredTemplateField(TemplateFieldKey Field, IReadOnlyList<string> ReplacedBy)
{
    public bool IsReplacedBy(string fieldId) => ReplacedBy.Contains(fieldId, StringComparer.OrdinalIgnoreCase);
}

/// <param name="Kind">The kind of answer the field stores, described for template authors, for example "text".</param>
/// <param name="SemanticKey">The field's own <c>semanticKey</c>, or its field ID when it has none.</param>
/// <param name="ReportingKey">
/// The key reports use for the field across versions: <see cref="SemanticKey"/>, prefixed with the collection's
/// reporting key and a slash for fields in a collection.
/// </param>
public sealed record TemplateField(string Kind, string SemanticKey, string ReportingKey);

/// <summary>
/// The fields a template version defines, read from its JSON: fields on task pages, collections (the
/// <c>fieldId</c> of each multi-collection or derived-collection flow) and the fields on each flow's pages, plus
/// the <c>retiredFields</c> the author declared. Each field has a kind; a field can only keep its ID while its
/// kind stays the same. A field's optional <c>semanticKey</c> (on the field, or on the flow for a collection) lets a
/// renamed field carry on the old field's reporting key.
/// </summary>
public sealed class TemplateFieldInventory
{
    public const string CollectionKind = "a collection of items";

    private static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private readonly Dictionary<TemplateFieldKey, TemplateField> fields = new();
    private readonly List<RetiredTemplateField> retired = [];

    private TemplateFieldInventory()
    {
    }

    public IReadOnlyDictionary<TemplateFieldKey, TemplateField> Fields => fields;

    public IReadOnlyList<RetiredTemplateField> Retired => retired;

    public bool IsRetired(TemplateFieldKey field) => RetirementOf(field) is not null;

    public RetiredTemplateField? RetirementOf(TemplateFieldKey field) => retired.FirstOrDefault(r => r.Field.Equals(field));

    public bool DefinesFieldId(string fieldId) =>
        fields.Keys.Any(k => string.Equals(k.FieldId, fieldId, StringComparison.OrdinalIgnoreCase));

    /// <returns>False when the schema isn't a JSON object.</returns>
    public static bool TryParse(string jsonSchema, out TemplateFieldInventory inventory)
    {
        inventory = new TemplateFieldInventory();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(jsonSchema, Options);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            inventory.Read(document.RootElement);
            return true;
        }
    }

    private void Read(JsonElement template)
    {
        foreach (var task in Items(template, "taskGroups").SelectMany(group => Items(group, "tasks")))
        {
            AddPageFields(task, string.Empty, string.Empty);

            if (Property(task, "summary") is not { } summary)
            {
                continue;
            }

            foreach (var flow in Items(summary, "flows").Concat(Items(summary, "derivedFlows")))
            {
                if (Text(flow, "fieldId") is not { } collectionId)
                {
                    continue;
                }

                var key = new TemplateFieldKey(string.Empty, collectionId);
                var semanticKey = Text(flow, "semanticKey") ?? collectionId;
                fields.TryAdd(key, new TemplateField(CollectionKind, semanticKey, semanticKey));
                AddPageFields(flow, collectionId, fields[key].ReportingKey);
            }
        }

        foreach (var declaration in Items(template, "retiredFields"))
        {
            if (Text(declaration, "fieldId") is not { } fieldId)
            {
                continue;
            }

            var replacedBy = Items(declaration, "replacedBy")
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!.Trim())
                .Where(id => id.Length > 0)
                .ToList();

            retired.Add(new RetiredTemplateField(new TemplateFieldKey(Text(declaration, "parentFieldId") ?? string.Empty, fieldId), replacedBy));
        }
    }

    private void AddPageFields(JsonElement owner, string parentFieldId, string parentReportingKey)
    {
        foreach (var field in Items(owner, "pages").SelectMany(page => Items(page, "fields")))
        {
            if (Text(field, "fieldId") is { } fieldId)
            {
                var semanticKey = Text(field, "semanticKey") ?? fieldId;
                var reportingKey = parentReportingKey.Length == 0 ? semanticKey : $"{parentReportingKey}/{semanticKey}";
                fields.TryAdd(new TemplateFieldKey(parentFieldId, fieldId), new TemplateField(KindOf(field), semanticKey, reportingKey));
            }
        }
    }

    /// <summary>
    /// Field types that store the same shape of answer share a kind, so a text box can become a text area, or
    /// radios a select, without a new ID. A lookup's kind includes its lookup configuration, because a lookup of
    /// trusts doesn't mean the same thing as a lookup of academies.
    /// </summary>
    private static string KindOf(JsonElement field)
    {
        var type = Text(field, "type")?.ToLowerInvariant() ?? string.Empty;
        return type switch
        {
            "text" or "textarea" or "text-area" or "email" or "character-count" => "text",
            "radios" or "select" => "a single choice",
            "checkboxes" => "multiple choices",
            "date" or "datetime" or "date-time" => "a date",
            "number" or "integer" or "decimal" => "a number",
            "boolean" => "a yes/no answer",
            "autocomplete" or "complexfield" => $"a lookup of \"{(Property(field, "complexField") is { } complex ? Text(complex, "id") : null) ?? string.Empty}\"",
            _ => $"a \"{type}\" answer",
        };
    }

    private static IEnumerable<JsonElement> Items(JsonElement owner, string name) =>
        Property(owner, name) is { ValueKind: JsonValueKind.Array } array
            ? array.EnumerateArray().Where(e => e.ValueKind is JsonValueKind.Object or JsonValueKind.String)
            : [];

    private static string? Text(JsonElement owner, string name) =>
        Property(owner, name) is { ValueKind: JsonValueKind.String } value && value.GetString()!.Trim() is { Length: > 0 } text
            ? text
            : null;

    private static JsonElement? Property(JsonElement owner, string name)
    {
        if (owner.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in owner.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }
}
