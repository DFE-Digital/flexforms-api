using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;

namespace GovUK.Dfe.FlexForms.Domain.Services;

/// <summary>
/// Merges role-default and user-specific permission grants into claim values.
/// A user grant overrides the role grant only for the same
/// <c>ResourceType</c> + <c>ResourceKey</c> + <c>AccessType</c>. Other role
/// access types remain effective, so adding an application or template Read
/// grant cannot accidentally remove role-derived Write or Manage access.
/// Template (form) access is stored as <see cref="ResourceType.Template"/> user grants.
/// </summary>
public static class PermissionClaimMerger
{
    public readonly record struct Grant(ResourceType ResourceType, string ResourceKey, AccessType AccessType);

    public static IReadOnlyList<string> Merge(
        IEnumerable<Grant> roleGrants,
        IEnumerable<Grant> userGrants)
    {
        var userGrantsByAccessType = new HashSet<(ResourceType Type, string Key, AccessType AccessType)>(
            userGrants.Select(g => (g.ResourceType, NormalizeKey(g.ResourceKey), g.AccessType)),
            TypeKeyAccessComparer.Instance);

        var claims = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var grant in roleGrants)
        {
            var key = (grant.ResourceType, NormalizeKey(grant.ResourceKey), grant.AccessType);
            if (userGrantsByAccessType.Contains(key))
                continue;

            claims.Add(Format(grant.ResourceType, grant.ResourceKey, grant.AccessType));
        }

        foreach (var grant in userGrants)
            claims.Add(Format(grant.ResourceType, grant.ResourceKey, grant.AccessType));

        return claims.ToList();
    }

    private static string NormalizeKey(string key) => key.Trim();

    private static string Format(ResourceType resourceType, string resourceKey, AccessType accessType) =>
        $"{resourceType}:{resourceKey.Trim()}:{accessType}";

    private sealed class TypeKeyAccessComparer : IEqualityComparer<(ResourceType Type, string Key, AccessType AccessType)>
    {
        public static readonly TypeKeyAccessComparer Instance = new();

        public bool Equals((ResourceType Type, string Key, AccessType AccessType) x, (ResourceType Type, string Key, AccessType AccessType) y) =>
            x.Type == y.Type
            && x.AccessType == y.AccessType
            && string.Equals(x.Key, y.Key, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((ResourceType Type, string Key, AccessType AccessType) obj) =>
            HashCode.Combine(
                obj.Type,
                obj.AccessType,
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Key));
    }
}
