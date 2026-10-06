namespace GovUK.Dfe.FlexForms.Domain.Services;

/// <summary>
/// Compares a new template version with the latest existing one, and with every earlier one for reused IDs:
/// <list type="bullet">
/// <item>A field can't disappear unless the new version lists it under <c>retiredFields</c>. Renaming a field ID
/// is a removal plus an addition, so it has to be declared too, with the new fields as <c>replacedBy</c>.</item>
/// <item>A field ID used by an earlier version can't come back for a new field.</item>
/// <item>A field keeps its kind; changing what it stores needs a new field ID.</item>
/// </list>
/// Versions whose schema can't be read are skipped, so legacy templates aren't blocked.
/// </summary>
public sealed class TemplateFieldCompatibilityPolicy : ITemplateFieldCompatibilityPolicy
{
    public TemplateFieldCompatibilityResult Evaluate(IReadOnlyList<TemplateVersionSchema> earlierVersions, string newJsonSchema)
    {
        if (!TemplateFieldInventory.TryParse(newJsonSchema, out var next))
        {
            return TemplateFieldCompatibilityResult.Compatible();
        }

        var earlier = new List<(string VersionNumber, TemplateFieldInventory Inventory)>();
        foreach (var version in earlierVersions)
        {
            if (TemplateFieldInventory.TryParse(version.JsonSchema, out var inventory))
            {
                earlier.Add((version.VersionNumber, inventory));
            }
        }

        if (earlier.Count == 0)
        {
            return TemplateFieldCompatibilityResult.Compatible();
        }

        var (latestVersion, latest) = earlier[^1];
        var violations = new List<string>();

        foreach (var field in latest.Fields.Keys.Where(f => !next.Fields.ContainsKey(f)))
        {
            if (!next.IsRetired(field) && !next.IsRetired(CollectionOf(field)))
            {
                violations.Add(
                    $"Field {field} is in version {latestVersion} but missing from this version. A field ID can't be removed or renamed. "
                    + $"Either put the field back with the same ID, or retire it by adding {field.ToRetiredFieldJson()} to the \"retiredFields\" list at the top of the template. "
                    + "If new fields replace it, name them in \"replacedBy\", for example \"replacedBy\": [\"firstName\", \"lastName\"].");
            }
        }

        foreach (var field in next.Fields.Keys.Where(f => !latest.Fields.ContainsKey(f)))
        {
            var reusedCollection = field.ParentFieldId.Length > 0 && !latest.Fields.ContainsKey(CollectionOf(field))
                && earlier.Any(e => e.Inventory.Fields.ContainsKey(CollectionOf(field)));
            if (reusedCollection)
            {
                continue;
            }

            if (earlier.LastOrDefault(e => e.Inventory.Fields.ContainsKey(field)) is { Inventory: not null } used)
            {
                violations.Add(
                    $"Field {field} was used in version {used.VersionNumber} and later removed, so its ID can't be reused for a new field. "
                    + "Give the new field a different ID.");
            }
        }

        foreach (var (field, kind) in next.Fields)
        {
            if (latest.Fields.TryGetValue(field, out var previousKind) && previousKind != kind)
            {
                violations.Add(
                    $"Field {field} stores {previousKind} in version {latestVersion} but {kind} in this version. A field can't change the kind of answer it stores. "
                    + $"Give the changed field a new ID, and retire the old one by adding {field.ToRetiredFieldJson(["<new field ID>"])} to \"retiredFields\".");
            }
        }

        foreach (var declaration in next.Retired)
        {
            if (next.Fields.ContainsKey(declaration.Field))
            {
                violations.Add(
                    $"Field {declaration.Field} is listed in \"retiredFields\" but is still in the template. "
                    + "Remove it from \"retiredFields\" to keep the field, or remove the field to retire it.");
            }
            else if (!earlier.Any(e => e.Inventory.Fields.ContainsKey(declaration.Field)))
            {
                violations.Add(
                    $"\"retiredFields\" lists field {declaration.Field}, but no earlier version of the template has it. "
                    + "Check the spelling of \"fieldId\", and of \"parentFieldId\" if the field is in a collection.");
            }

            foreach (var replacement in declaration.ReplacedBy.Where(id => !next.DefinesFieldId(id)))
            {
                violations.Add(
                    $"Field {declaration.Field} is retired with \"replacedBy\" naming \"{replacement}\", but this version has no field with that ID. "
                    + "Check the spelling, or add the field.");
            }
        }

        return violations.Count == 0
            ? TemplateFieldCompatibilityResult.Compatible()
            : TemplateFieldCompatibilityResult.Incompatible(violations);
    }

    private static TemplateFieldKey CollectionOf(TemplateFieldKey field) => new(string.Empty, field.ParentFieldId);
}
