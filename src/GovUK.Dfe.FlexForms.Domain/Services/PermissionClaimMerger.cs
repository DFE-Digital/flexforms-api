using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;

namespace GovUK.Dfe.FlexForms.Domain.Services;

/// <summary>
/// Merges role-default and user-specific permission grants into claim values.
/// A user grant overrides the role grant only for the same
/// <c>ResourceType</c> + <c>ResourceKey</c> + <c>AccessType</c>. Other role
/// access types remain effective, so adding a template Read grant cannot
/// remove role-derived Write or Manage access.
/// A direct Read grant on one application or its files, without the matching
/// Write or Delete grant, suppresses the role's Write and Delete grants for
/// that same application. That keeps a view-only contributor invite read-only
/// when the assigned role also contains edit grants for that application.
/// When the user has any concrete Application grant on an application id
/// (contributor invite), role ApplicationFiles grants for that same id are
/// suppressed unless the user also has the matching file grant — so unticked
/// file boxes are not filled by a concrete role file Read. Tenant-wide
/// <c>ApplicationFiles:Any</c> stays in the claim set for other applications;
/// <see cref="PermissionClaimEvaluator"/> uses invite-scope marker claims to
/// ignore Any on invite-scoped applications only.
/// Tenant Admin and SuperAdmin still edit through their role, which is checked
/// separately from these claims.
/// Template (form) access is stored as <see cref="ResourceType.Template"/> user grants.
/// </summary>
public static class PermissionClaimMerger
{
    public const string InviteScopeResourceType = "ContributorInvite";
    public const string InviteScopeAccessMarker = "Scope";

    public readonly record struct Grant(ResourceType ResourceType, string ResourceKey, AccessType AccessType);

    public static string FormatInviteScopeClaim(string applicationId) =>
        $"{InviteScopeResourceType}:{applicationId.Trim()}:{InviteScopeAccessMarker}";

    public static bool IsInviteScopeClaim(string claimValue)
    {
        var parts = claimValue.Split(':', 3);
        return parts.Length == 3
               && string.Equals(parts[0], InviteScopeResourceType, StringComparison.OrdinalIgnoreCase)
               && string.Equals(parts[2], InviteScopeAccessMarker, StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryGetInviteScopeApplicationId(string claimValue, out string applicationId)
    {
        applicationId = string.Empty;
        var parts = claimValue.Split(':', 3);
        if (parts.Length != 3
            || !string.Equals(parts[0], InviteScopeResourceType, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parts[2], InviteScopeAccessMarker, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(parts[1]))
        {
            return false;
        }

        applicationId = parts[1].Trim();
        return true;
    }

    public static IReadOnlyList<string> Merge(
        IEnumerable<Grant> roleGrants,
        IEnumerable<Grant> userGrants)
    {
        var userGrantList = userGrants.ToList();
        var userGrantsByAccessType = new HashSet<(ResourceType Type, string Key, AccessType AccessType)>(
            userGrantList.Select(g => (g.ResourceType, NormalizeKey(g.ResourceKey), g.AccessType)),
            TypeKeyAccessComparer.Instance);

        var inviteScopedApplicationIds = userGrantList
            .Where(g => g.ResourceType == ResourceType.Application
                        && !string.Equals(
                            NormalizeKey(g.ResourceKey),
                            PermissionConstants.AnyResourceKey,
                            StringComparison.OrdinalIgnoreCase))
            .Select(g => NormalizeKey(g.ResourceKey))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var claims = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var grant in roleGrants)
        {
            var key = (grant.ResourceType, NormalizeKey(grant.ResourceKey), grant.AccessType);
            if (userGrantsByAccessType.Contains(key)
                || IsSuppressedByReadOnlyGrant(grant, userGrantsByAccessType)
                || IsSuppressedByInviteFileRestriction(grant, inviteScopedApplicationIds, userGrantsByAccessType))
                continue;

            claims.Add(Format(grant.ResourceType, grant.ResourceKey, grant.AccessType));
        }

        foreach (var grant in userGrantList)
            claims.Add(Format(grant.ResourceType, grant.ResourceKey, grant.AccessType));

        // Provenance markers: only emitted from user-level Application grants (invite), never from role.
        foreach (var applicationId in inviteScopedApplicationIds)
            claims.Add(FormatInviteScopeClaim(applicationId));

        return claims.ToList();
    }

    private static bool IsSuppressedByReadOnlyGrant(
        Grant grant,
        HashSet<(ResourceType Type, string Key, AccessType AccessType)> userGrantsByAccessType)
    {
        if (grant.ResourceType is not (ResourceType.Application or ResourceType.ApplicationFiles))
            return false;

        if (grant.AccessType is not (AccessType.Write or AccessType.Delete))
            return false;

        var key = NormalizeKey(grant.ResourceKey);
        if (string.Equals(key, PermissionConstants.AnyResourceKey, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!userGrantsByAccessType.Contains((grant.ResourceType, key, AccessType.Read)))
            return false;

        return !userGrantsByAccessType.Contains((grant.ResourceType, key, grant.AccessType));
    }

    /// <summary>
    /// On an invite-scoped application, concrete role ApplicationFiles grants do not apply
    /// unless the invite also granted that access. <c>Any</c> is left to the evaluator + scope marker.
    /// </summary>
    private static bool IsSuppressedByInviteFileRestriction(
        Grant grant,
        HashSet<string> inviteScopedApplicationIds,
        HashSet<(ResourceType Type, string Key, AccessType AccessType)> userGrantsByAccessType)
    {
        if (grant.ResourceType != ResourceType.ApplicationFiles)
            return false;

        var key = NormalizeKey(grant.ResourceKey);
        if (string.Equals(key, PermissionConstants.AnyResourceKey, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!inviteScopedApplicationIds.Contains(key))
            return false;

        return !userGrantsByAccessType.Contains((ResourceType.ApplicationFiles, key, grant.AccessType));
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
