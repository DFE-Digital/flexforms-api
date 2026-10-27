namespace GovUK.Dfe.FlexForms.Domain.Tenancy;

/// <summary>
/// A setting category must live either under the <c>Shared</c> target or under app-specific
/// targets (<c>Api</c> / <c>Web</c>), never both. Each app flattens <c>Shared</c> plus its own
/// target into one configuration, so an overlap produces duplicate keys: the API tenant cache
/// refresh fails outright, and the Web app silently picks one row over the other.
/// </summary>
public static class TenantSettingTargetOverlap
{
    public const string SharedTarget = "Shared";

    public static bool IsShared(string? target) =>
        string.Equals(target?.Trim(), SharedTarget, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the targets in <paramref name="existing"/> that would overlap with a new
    /// <paramref name="category"/> row under <paramref name="target"/>.
    /// </summary>
    public static IReadOnlyList<string> FindConflictingTargets(
        string category,
        string target,
        IEnumerable<(string Category, string Target)> existing)
    {
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(target))
        {
            return [];
        }

        var newIsShared = IsShared(target);

        return existing
            .Where(e => string.Equals(e.Category?.Trim(), category.Trim(), StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(e.Target?.Trim(), target.Trim(), StringComparison.OrdinalIgnoreCase)
                        && (newIsShared || IsShared(e.Target)))
            .Select(e => e.Target)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Describes every category in <paramref name="settings"/> that has both a <c>Shared</c>
    /// row and an app-specific row. Empty when the set is consistent.
    /// </summary>
    public static IReadOnlyList<string> FindOverlaps(IEnumerable<(string Category, string Target)> settings) =>
        settings
            .GroupBy(s => s.Category.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Any(s => IsShared(s.Target)) && g.Any(s => !IsShared(s.Target)))
            .Select(g => $"'{g.Key}' ({string.Join(" + ", g.Select(s => s.Target).Distinct(StringComparer.OrdinalIgnoreCase))})")
            .ToList();

    public static string DescribeConflict(string category, string target, IReadOnlyCollection<string> conflictingTargets) =>
        $"Setting '{category}' cannot be added under target '{target}' because it already exists under " +
        $"{string.Join(", ", conflictingTargets.Select(t => $"'{t}'"))}. A category must be either Shared or " +
        "app-specific (Api/Web), not both. Edit the existing row, or delete it first.";
}

/// <summary>
/// Thrown when a write would leave a tenant with the same category under both
/// <c>Shared</c> and an app-specific target. See <see cref="TenantSettingTargetOverlap"/>.
/// </summary>
public sealed class TenantSettingTargetConflictException(string message) : InvalidOperationException(message);
