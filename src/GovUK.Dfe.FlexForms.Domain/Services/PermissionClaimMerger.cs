using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;

namespace GovUK.Dfe.FlexForms.Domain.Services;

/// <summary>
/// Merges role-default and user-specific permission grants into claim values.
/// When a user has any grant for a given <c>ResourceType</c> + <c>ResourceKey</c>,
/// role grants for that same key are omitted (user overrides role).
/// </summary>
public static class PermissionClaimMerger
{
    public readonly record struct Grant(ResourceType ResourceType, string ResourceKey, AccessType AccessType);

    public static IReadOnlyList<string> Merge(
        IEnumerable<Grant> roleGrants,
        IEnumerable<Grant> userGrants,
        IEnumerable<(Guid TemplateId, AccessType AccessType)> templateGrants)
    {
        var userKeys = new HashSet<(ResourceType Type, string Key)>(
            userGrants.Select(g => (g.ResourceType, NormalizeKey(g.ResourceKey))),
            TypeKeyComparer.Instance);

        var claims = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var grant in roleGrants)
        {
            var key = (grant.ResourceType, NormalizeKey(grant.ResourceKey));
            if (userKeys.Contains(key))
                continue;

            claims.Add(Format(grant.ResourceType, grant.ResourceKey, grant.AccessType));
        }

        foreach (var grant in userGrants)
            claims.Add(Format(grant.ResourceType, grant.ResourceKey, grant.AccessType));

        foreach (var (templateId, accessType) in templateGrants)
            claims.Add($"Template:{templateId}:{accessType}");

        return claims.ToList();
    }

    private static string NormalizeKey(string key) => key.Trim();

    private static string Format(ResourceType resourceType, string resourceKey, AccessType accessType) =>
        $"{resourceType}:{resourceKey.Trim()}:{accessType}";

    private sealed class TypeKeyComparer : IEqualityComparer<(ResourceType Type, string Key)>
    {
        public static readonly TypeKeyComparer Instance = new();

        public bool Equals((ResourceType Type, string Key) x, (ResourceType Type, string Key) y) =>
            x.Type == y.Type
            && string.Equals(x.Key, y.Key, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((ResourceType Type, string Key) obj) =>
            HashCode.Combine(obj.Type, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Key));
    }
}
