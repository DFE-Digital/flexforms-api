namespace GovUK.Dfe.FlexForms.Domain.Services;

/// <summary>
/// Compares a new template version with the latest existing one, and with every earlier one for reused IDs and
/// reporting keys, so the author owns every mapping that reporting relies on:
/// <list type="bullet">
/// <item>A field can't disappear unless the new version lists it under <c>retiredFields</c>. Renaming a field ID
/// is a removal plus an addition, so it has to be declared too, with the new fields as <c>replacedBy</c>.</item>
/// <item>A field ID used by an earlier version can't come back for a new field.</item>
/// <item>A field keeps its kind; changing what it stores needs a new field ID.</item>
/// <item>A field keeps its reporting key (its <c>semanticKey</c>, or its ID). A new field can only take over the
/// reporting key of a field retired in the same version, when it stores the same kind of answer and is named in
/// that field's <c>replacedBy</c>. Reporting keys are unique within a version and never reused once dropped.</item>
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

        var check = new Check(earlier, next);
        check.Run();
        return check.Violations.Count == 0
            ? TemplateFieldCompatibilityResult.Compatible()
            : TemplateFieldCompatibilityResult.Incompatible(check.Violations);
    }

    private sealed class Check(List<(string VersionNumber, TemplateFieldInventory Inventory)> earlier, TemplateFieldInventory next)
    {
        private readonly string latestVersion = earlier[^1].VersionNumber;
        private readonly TemplateFieldInventory latest = earlier[^1].Inventory;
        private readonly HashSet<TemplateFieldKey> flagged = [];

        public List<string> Violations { get; } = [];

        public void Run()
        {
            CheckRemovedFields();
            CheckAddedFields();
            CheckKeptFields();
            CheckReportingKeysAreUnique();
            CheckRetirements();
        }

        private void CheckRemovedFields()
        {
            foreach (var (field, previous) in latest.Fields.Where(f => !next.Fields.ContainsKey(f.Key)))
            {
                if (!next.IsRetired(field) && !next.IsRetired(CollectionOf(field)))
                {
                    Add(field,
                        $"Field {field} is in version {latestVersion} but missing from this version. A field ID can't be removed or renamed. "
                        + $"Either put the field back with the same ID, or retire it by adding {field.ToRetiredFieldJson()} to the \"retiredFields\" list at the top of the template. "
                        + "If new fields replace it, name them in \"replacedBy\", for example \"replacedBy\": [\"firstName\", \"lastName\"]. "
                        + $"If you're only renaming it, also set \"semanticKey\": \"{previous.SemanticKey}\" on the renamed field so reports carry on as one column.");
                }
            }
        }

        private void CheckAddedFields()
        {
            foreach (var (field, definition) in next.Fields.Where(f => !latest.Fields.ContainsKey(f.Key)))
            {
                if (IsInsideFlaggedOrReusedCollection(field))
                {
                    continue;
                }

                if (earlier.LastOrDefault(e => e.Inventory.Fields.ContainsKey(field)) is { Inventory: not null } used)
                {
                    Add(field,
                        $"Field {field} was used in version {used.VersionNumber} and later removed, so its ID can't be reused for a new field. "
                        + "Give the new field a different ID.");
                    continue;
                }

                CheckTakenOverReportingKey(field, definition);
            }
        }

        /// <summary>A new field whose reporting key belonged to another field: allowed only as a declared one-to-one replacement.</summary>
        private void CheckTakenOverReportingKey(TemplateFieldKey field, TemplateField definition)
        {
            var (previousField, previous) = latest.Fields.FirstOrDefault(f => SameKey(f.Value.ReportingKey, definition.ReportingKey));
            if (previous is null)
            {
                if (earlier.LastOrDefault(e => e.Inventory.Fields.Values.Any(f => SameKey(f.ReportingKey, definition.ReportingKey))) is { Inventory: not null } used)
                {
                    Add(field,
                        $"Field {field} uses the reporting key \"{definition.ReportingKey}\", which version {used.VersionNumber} used for a field that has since been removed. "
                        + "A reporting key can't be reused. Give the field a different \"semanticKey\".");
                }

                return;
            }

            if (next.Fields.ContainsKey(previousField))
            {
                return;
            }

            if (previous.Kind != definition.Kind)
            {
                Add(field,
                    $"Field {field} takes over the reporting key \"{definition.ReportingKey}\" from field {previousField}, but stores {definition.Kind} where {previousField} stored {previous.Kind}. "
                    + "A replacement can only carry on a reporting key if it stores the same kind of answer. Give the field a different \"semanticKey\".");
                return;
            }

            var retirement = next.RetirementOf(previousField);
            var collectionTakenOver = field.ParentFieldId.Length > 0 && next.RetirementOf(CollectionOf(previousField)) is { } collectionRetirement
                && collectionRetirement.IsReplacedBy(field.ParentFieldId);
            if (retirement?.IsReplacedBy(field.FieldId) != true && !collectionTakenOver)
            {
                Add(field,
                    $"Field {field} takes over the reporting key \"{definition.ReportingKey}\" from field {previousField}, but the retirement of {previousField} doesn't name it as a replacement. "
                    + $"Add \"{field.FieldId}\" to its \"replacedBy\", for example {previousField.ToRetiredFieldJson([field.FieldId])}.");
            }
        }

        private void CheckKeptFields()
        {
            foreach (var (field, definition) in next.Fields)
            {
                if (!latest.Fields.TryGetValue(field, out var previous))
                {
                    continue;
                }

                if (previous.Kind != definition.Kind)
                {
                    Add(field,
                        $"Field {field} stores {previous.Kind} in version {latestVersion} but {definition.Kind} in this version. A field can't change the kind of answer it stores. "
                        + $"Give the changed field a new ID, and retire the old one by adding {field.ToRetiredFieldJson(["<new field ID>"])} to \"retiredFields\".");
                }
                else if (!SameKey(previous.SemanticKey, definition.SemanticKey))
                {
                    Add(field,
                        $"Field {field} has the semanticKey \"{previous.SemanticKey}\" in version {latestVersion} but \"{definition.SemanticKey}\" in this version. "
                        + $"Reports use it to line answers up across versions, so it can't change. Set \"semanticKey\": \"{previous.SemanticKey}\" on the field.");
                }
            }
        }

        private void CheckReportingKeysAreUnique()
        {
            foreach (var group in next.Fields.GroupBy(f => f.Value.ReportingKey, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            {
                var names = string.Join(" and ", group.Select(f => f.Key.ToString()));
                Violations.Add(
                    $"Fields {names} share the reporting key \"{group.Key}\". Every field needs its own reporting key, so give each a different \"semanticKey\" or field ID.");
            }

            foreach (var (field, definition) in next.Fields.Where(f => f.Value.SemanticKey.Contains('/')))
            {
                Add(field, $"Field {field} has the semanticKey \"{definition.SemanticKey}\". A semanticKey can't contain \"/\".");
            }
        }

        private void CheckRetirements()
        {
            foreach (var declaration in next.Retired)
            {
                if (next.Fields.ContainsKey(declaration.Field))
                {
                    Violations.Add(
                        $"Field {declaration.Field} is listed in \"retiredFields\" but is still in the template. "
                        + "Remove it from \"retiredFields\" to keep the field, or remove the field to retire it.");
                }
                else if (!earlier.Any(e => e.Inventory.Fields.ContainsKey(declaration.Field)))
                {
                    Violations.Add(
                        $"\"retiredFields\" lists field {declaration.Field}, but no earlier version of the template has it. "
                        + "Check the spelling of \"fieldId\", and of \"parentFieldId\" if the field is in a collection.");
                }

                foreach (var replacement in declaration.ReplacedBy.Where(id => !next.DefinesFieldId(id)))
                {
                    Violations.Add(
                        $"Field {declaration.Field} is retired with \"replacedBy\" naming \"{replacement}\", but this version has no field with that ID. "
                        + "Check the spelling, or add the field.");
                }
            }
        }

        /// <summary>Nested fields of a collection that is already reported (or whose ID is reused) would only repeat the problem.</summary>
        private bool IsInsideFlaggedOrReusedCollection(TemplateFieldKey field)
        {
            if (field.ParentFieldId.Length == 0)
            {
                return false;
            }

            var collection = CollectionOf(field);
            return flagged.Contains(collection)
                || (!latest.Fields.ContainsKey(collection) && earlier.Any(e => e.Inventory.Fields.ContainsKey(collection)));
        }

        private void Add(TemplateFieldKey field, string violation)
        {
            if (field.ParentFieldId.Length > 0 && flagged.Contains(CollectionOf(field)))
            {
                return;
            }

            flagged.Add(field);
            Violations.Add(violation);
        }

        private static bool SameKey(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static TemplateFieldKey CollectionOf(TemplateFieldKey field) => new(string.Empty, field.ParentFieldId);
    }
}
