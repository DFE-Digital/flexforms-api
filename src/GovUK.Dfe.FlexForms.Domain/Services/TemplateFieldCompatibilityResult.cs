namespace GovUK.Dfe.FlexForms.Domain.Services;

public sealed class TemplateFieldCompatibilityResult
{
    public bool IsCompatible => Violations.Count == 0;
    public IReadOnlyList<string> Violations { get; }

    private TemplateFieldCompatibilityResult(IReadOnlyList<string> violations)
    {
        Violations = violations;
    }

    public static TemplateFieldCompatibilityResult Compatible() => new([]);

    public static TemplateFieldCompatibilityResult Incompatible(IReadOnlyList<string> violations) => new(violations);

    public const string Summary =
        "This version can't be saved because it changes fields that existing applications use, which would break reporting across versions.";

    /// <summary>The summary, then one line per violation, so a client can show each problem separately.</summary>
    public string ToErrorMessage() =>
        IsCompatible
            ? string.Empty
            : string.Join("\n", [Summary, .. Violations]);
}
