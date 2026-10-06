namespace GovUK.Dfe.FlexForms.Domain.Services;

public sealed record TemplateVersionSchema(string VersionNumber, string JsonSchema);

/// <summary>
/// Keeps field IDs stable across the versions of a template, so reporting can line answers up across versions.
/// </summary>
public interface ITemplateFieldCompatibilityPolicy
{
    /// <param name="earlierVersions">The template's existing versions, oldest first.</param>
    /// <param name="newJsonSchema">The schema of the version being added.</param>
    TemplateFieldCompatibilityResult Evaluate(IReadOnlyList<TemplateVersionSchema> earlierVersions, string newJsonSchema);
}
