using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;

namespace GovUK.Dfe.FlexForms.Domain.Common;

/// <summary>
/// Default <c>RolePermissions</c> seeded for system roles.
/// User-specific grants (email-scoped User/Notifications, per-template access) remain on the user
/// as overrides via <c>Permissions</c> (<c>ResourceType.Template</c> for form access).
/// </summary>
public static class SystemRolePermissionDefaults
{
    public sealed record DefaultGrant(ResourceType ResourceType, string ResourceKey, AccessType AccessType);

    /// <summary>
    /// Tenant-wide defaults for a canonical system role name. SuperAdmin returns none —
    /// full access is the <c>IsInRole(SuperAdmin)</c> special case. User and custom roles
    /// have no hardcoded defaults (configure via RolePermissions).
    /// </summary>
    public static IReadOnlyList<DefaultGrant> ForRole(string canonicalRoleName)
    {
        // SuperAdmin / User / custom: no hardcoded defaults.
        _ = canonicalRoleName;
        return Array.Empty<DefaultGrant>();
    }
}
