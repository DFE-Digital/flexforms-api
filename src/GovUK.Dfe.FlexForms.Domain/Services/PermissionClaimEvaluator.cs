using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Domain.Services;

/// <summary>
/// Evaluates resource permission claims on a <see cref="ClaimsPrincipal"/>, including role-based
/// capabilities and tenant-wide wildcard permissions.
/// </summary>
public static class PermissionClaimEvaluator
{
    public const string PermissionClaimType = "permission";

    /// <summary>
    /// Returns true when the user has platform-wide administrative access.
    /// </summary>
    public static bool HasPlatformAdminAccess(ClaimsPrincipal user) =>
        user.IsInRole(RoleNames.SuperAdmin);

    /// <summary>
    /// Returns true when the user has tenant administrative access.
    /// </summary>
    public static bool HasTenantAdminAccess(ClaimsPrincipal user) =>
        HasPlatformAdminAccess(user)
        || user.IsInRole(RoleNames.Admin);

    /// <summary>
    /// Returns true when the user has tenant administrative access.
    /// Kept as the broad bypass used by tenant-scoped authorization handlers.
    /// </summary>
    public static bool HasFullAdminAccess(ClaimsPrincipal user) =>
        HasTenantAdminAccess(user);

    /// <summary>
    /// Returns true when the principal is an interactive SuperAdmin (platform admin) user JWT,
    /// not a machine/service identity.
    /// </summary>
    public static bool IsInteractivePlatformAdmin(ClaimsPrincipal user)
    {
        if (!HasPlatformAdminAccess(user))
        {
            return false;
        }

        if (user.HasClaim(c =>
                c.Type == TenantAuthClaimTypes.IsService
                && string.Equals(c.Value, "true", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var email = user.FindFirst(ClaimTypes.Email)?.Value
            ?? user.FindFirst("email")?.Value;
        return !string.IsNullOrWhiteSpace(email);
    }

    /// <summary>
    /// Returns true when the principal is an interactive Admin user (user JWT), not a machine/
    /// service identity. Client-credentials and other <c>is_service=true</c> callers are rejected
    /// even if they were given an Admin role claim via AuthProviders.
    /// </summary>
    public static bool IsInteractiveTenantAdmin(ClaimsPrincipal user)
    {
        if (!HasFullAdminAccess(user))
        {
            return false;
        }

        if (user.HasClaim(c =>
                c.Type == TenantAuthClaimTypes.IsService
                && string.Equals(c.Value, "true", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var email = user.FindFirst(ClaimTypes.Email)?.Value
            ?? user.FindFirst("email")?.Value;
        return !string.IsNullOrWhiteSpace(email);
    }

    /// <summary>
    /// Returns true when the user can read all applications in the current tenant
    /// (SuperAdmin, or an Application:Any:Read permission claim from a role/user grant).
    /// </summary>
    public static bool CanReadAllApplications(ClaimsPrincipal user) =>
        HasFullAdminAccess(user)
        || HasPermissionClaim(user, ResourceType.Application, PermissionConstants.AnyResourceKey, AccessType.Read);

    /// <summary>
    /// Returns true when the user can administer templates in the current tenant
    /// (create, edit versions, publish). Tenant admins, or any <c>Template:*:Manage</c> claim.
    /// </summary>
    public static bool CanManageTemplates(ClaimsPrincipal user) =>
        HasTenantAdminAccess(user)
        || HasAnyPermissionClaim(user, ResourceType.Template, AccessType.Manage);

    /// <summary>
    /// Returns true when the user can administer the specified template in the current tenant.
    /// </summary>
    public static bool CanManageTemplate(ClaimsPrincipal user, string templateId) =>
        HasTenantAdminAccess(user)
        || HasPermissionClaim(user, ResourceType.Template, templateId, AccessType.Manage)
        || HasPermissionClaim(user, ResourceType.Template, PermissionConstants.AnyResourceKey, AccessType.Manage);

    /// <summary>
    /// Returns true when the user can administer users in the current tenant.
    /// Tenant admins, or any <c>User:*:Manage</c> claim.
    /// </summary>
    public static bool CanManageUsers(ClaimsPrincipal user) =>
        HasTenantAdminAccess(user)
        || HasAnyPermissionClaim(user, ResourceType.User, AccessType.Manage);

    /// <summary>
    /// Returns true when the user can write any application in the tenant (Admin only).
    /// </summary>
    public static bool CanWriteAnyApplication(ClaimsPrincipal user) =>
        HasFullAdminAccess(user);

    /// <summary>
    /// Returns true when the user can read the specified application.
    /// </summary>
    public static bool CanReadApplication(ClaimsPrincipal user, string applicationId) =>
        HasFullAdminAccess(user)
        || HasPermissionClaimOrTenantWide(user, ResourceType.Application, applicationId, AccessType.Read);

    /// <summary>
    /// Returns true when the user can write the specified application (exact permission or Admin only).
    /// Wildcard write grants are intentionally excluded to avoid elevating standard users.
    /// </summary>
    public static bool CanWriteApplication(ClaimsPrincipal user, string applicationId) =>
        HasFullAdminAccess(user)
        || HasPermissionClaim(user, ResourceType.Application, applicationId, AccessType.Write);

    /// <summary>
    /// Returns true when the user can read files for the specified application.
    /// </summary>
    public static bool CanReadApplicationFiles(ClaimsPrincipal user, string applicationId) =>
        HasFullAdminAccess(user)
        || HasPermissionClaimOrTenantWide(user, ResourceType.ApplicationFiles, applicationId, AccessType.Read);

    /// <summary>
    /// Returns true when the user can write files for the specified application (exact permission or Admin only).
    /// </summary>
    public static bool CanWriteApplicationFiles(ClaimsPrincipal user, string applicationId) =>
        HasFullAdminAccess(user)
        || HasPermissionClaim(user, ResourceType.ApplicationFiles, applicationId, AccessType.Write);

    /// <summary>
    /// Returns true when the user can delete files for the specified application (exact permission or Admin only).
    /// </summary>
    public static bool CanDeleteApplicationFiles(ClaimsPrincipal user, string applicationId) =>
        HasFullAdminAccess(user)
        || HasPermissionClaim(user, ResourceType.ApplicationFiles, applicationId, AccessType.Delete);

    /// <summary>
    /// Returns true when a machine identity may report a validation result for the template.
    /// Does not honour <see cref="HasFullAdminAccess"/> — this grant is never implied by Admin.
    /// </summary>
    public static bool CanWriteFileValidation(ClaimsPrincipal user, string templateId) =>
        HasPermissionClaimOrTenantWide(user, ResourceType.FileValidation, templateId, AccessType.Write);

    /// <summary>
    /// Returns true when the principal has any FileValidation Write grant (tenant-wide or template-scoped).
    /// </summary>
    public static bool CanWriteAnyFileValidation(ClaimsPrincipal user) =>
        HasAnyPermissionClaim(user, ResourceType.FileValidation, AccessType.Write);

    /// <summary>
    /// Returns true when the user has an exact or tenant-wide (<see cref="PermissionConstants.AnyResourceKey"/>)
    /// permission claim for the resource.
    /// </summary>
    public static bool HasPermissionClaimOrTenantWide(
        ClaimsPrincipal user,
        ResourceType resourceType,
        string resourceId,
        AccessType accessType) =>
        HasPermissionClaim(user, resourceType, resourceId, accessType)
        || HasPermissionClaim(user, resourceType, PermissionConstants.AnyResourceKey, accessType);

    /// <summary>
    /// Returns true when the user has an exact permission claim for the resource.
    /// </summary>
    public static bool HasPermissionClaim(
        ClaimsPrincipal user,
        ResourceType resourceType,
        string resourceId,
        AccessType accessType)
    {
        var expected = FormatPermissionClaim(resourceType, resourceId, accessType);
        return user.Claims.Any(c =>
            c.Type == PermissionClaimType
            && string.Equals(c.Value, expected, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns true when the user has at least one permission claim for the resource type and access level.
    /// </summary>
    public static bool HasAnyPermissionClaim(
        ClaimsPrincipal user,
        ResourceType resourceType,
        AccessType accessType)
    {
        var prefix = $"{resourceType}:";
        var suffix = $":{accessType}";

        return user.Claims.Any(c =>
            c.Type == PermissionClaimType
            && c.Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && c.Value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns true when the user has at least one non-wildcard permission claim for the resource type and access level.
    /// </summary>
    public static bool HasAnyExplicitPermissionClaim(
        ClaimsPrincipal user,
        ResourceType resourceType,
        AccessType accessType)
    {
        var prefix = $"{resourceType}:";
        var suffix = $":{accessType}";
        var wildcardSegment = $":{PermissionConstants.AnyResourceKey}:";

        return user.Claims.Any(c =>
            c.Type == PermissionClaimType
            && c.Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && c.Value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            && !c.Value.Contains(wildcardSegment, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Formats a permission claim value.
    /// </summary>
    public static string FormatPermissionClaim(ResourceType resourceType, string resourceId, AccessType accessType) =>
        $"{resourceType}:{resourceId}:{accessType}";
}
