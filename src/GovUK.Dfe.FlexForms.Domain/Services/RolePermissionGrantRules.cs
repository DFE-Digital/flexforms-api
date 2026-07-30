using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;

namespace GovUK.Dfe.FlexForms.Domain.Services;

/// <summary>
/// Rules for role permission grants: when <see cref="PermissionConstants.AnyResourceKey"/>
/// is allowed, when <see cref="AccessType.Manage"/> is allowed, and what a concrete
/// resource key should look like.
/// </summary>
/// <remarks>
/// Claim format is <c>{ResourceType}:{ResourceKey}:{AccessType}</c>. Matching is exact
/// (case-insensitive). Tenant-wide <c>Any</c> is only honoured where evaluators explicitly
/// call OrTenantWide helpers. <c>Template:Any:Manage</c> unlocks template administration
/// (create/edit/publish) and is separate from <c>Template:Any:Write</c> (create applications).
/// </remarks>
public static class RolePermissionGrantRules
{
    /// <summary>
    /// Validates shape and special-key usage for a role permission grant.
    /// Throws <see cref="ArgumentException"/> when invalid.
    /// </summary>
    public static void EnsureValid(ResourceType resourceType, string resourceKey, AccessType accessType)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
            throw new ArgumentException("Resource key is required for each permission grant.", nameof(resourceKey));

        var key = resourceKey.Trim();
        if (key.Length > 256)
            throw new ArgumentException("Resource key must be 256 characters or fewer.", nameof(resourceKey));

        if (accessType == AccessType.Manage)
        {
            EnsureManageAllowed(resourceType, key);
            if (string.Equals(key, PermissionConstants.AnyResourceKey, StringComparison.OrdinalIgnoreCase))
                return;
        }
        else if (string.Equals(key, PermissionConstants.AnyResourceKey, StringComparison.OrdinalIgnoreCase))
        {
            EnsureAnyAllowed(resourceType, accessType);
            return;
        }

        EnsureConcreteKeyShape(resourceType, key);
    }

    /// <summary>
    /// Validates a direct (user-owned) permission grant.
    /// Same shape rules as <see cref="EnsureValid"/>, but <see cref="AccessType.Manage"/>
    /// is never allowed — Manage must be assigned via a tenant-scoped role.
    /// </summary>
    public static void EnsureValidForUser(ResourceType resourceType, string resourceKey, AccessType accessType)
    {
        if (accessType == AccessType.Manage)
        {
            throw new ArgumentException(
                "Access type 'Manage' cannot be granted to an individual user. " +
                "Assign Manage via a tenant role instead.",
                nameof(accessType));
        }

        EnsureValid(resourceType, resourceKey, accessType);
    }

    /// <summary>
    /// Allowed tenant-wide <c>Any</c> grants:
    /// <list type="bullet">
    /// <item><description>Template — Write: create applications on any template</description></item>
    /// <item><description>Application — Read: CaseReader-style list/read all apps</description></item>
    /// <item><description>ApplicationFiles — Read: read files on all apps</description></item>
    /// <item><description>Template — Manage: tenant-wide template administration</description></item>
    /// <item><description>User — Manage: tenant-wide user administration</description></item>
    /// </list>
    /// </summary>
    public static bool IsTenantWideAnyAllowed(ResourceType resourceType, AccessType accessType) =>
        (resourceType == ResourceType.Template && accessType == AccessType.Write)
        || (resourceType == ResourceType.Application && accessType == AccessType.Read)
        || (resourceType == ResourceType.ApplicationFiles && accessType == AccessType.Read)
        || (resourceType == ResourceType.Template && accessType == AccessType.Manage)
        || (resourceType == ResourceType.User && accessType == AccessType.Manage);

    /// <summary>
    /// <c>Manage</c> is allowed for template and user administration.
    /// </summary>
    public static bool IsManageAllowed(ResourceType resourceType) =>
        resourceType == ResourceType.Template || resourceType == ResourceType.User;

    public static void EnsureAnyAllowed(ResourceType resourceType, AccessType accessType)
    {
        if (IsTenantWideAnyAllowed(resourceType, accessType))
            return;

        throw new ArgumentException(
            $"Resource key '{PermissionConstants.AnyResourceKey}' is only allowed for: " +
            "Template — Write (create applications on any template), " +
            "Application — Read (read all applications in the tenant), or " +
            "ApplicationFiles — Read (read files on all applications), " +
            "Template — Manage (administer templates in the tenant), or " +
            "User — Manage (administer users in the tenant). " +
            "For other combinations, use a specific resource id or email.",
            nameof(resourceType));
    }

    public static void EnsureManageAllowed(ResourceType resourceType, string resourceKey)
    {
        if (!IsManageAllowed(resourceType))
        {
            throw new ArgumentException(
                "Access type 'Manage' is only allowed for Template or User permissions.",
                nameof(resourceType));
        }

        if (string.Equals(resourceKey, PermissionConstants.AnyResourceKey, StringComparison.OrdinalIgnoreCase))
            return;
    }

    public static void EnsureConcreteKeyShape(ResourceType resourceType, string resourceKey)
    {
        switch (resourceType)
        {
            case ResourceType.Application:
            case ResourceType.ApplicationFiles:
            case ResourceType.Template:
            case ResourceType.File:
            case ResourceType.Task:
            case ResourceType.TaskGroup:
            case ResourceType.Page:
            case ResourceType.Field:
                if (!Guid.TryParse(resourceKey, out var id) || id == Guid.Empty)
                {
                    throw new ArgumentException(
                        $"{resourceType} resource key must be a valid non-empty GUID (the resource id), " +
                        $"or '{PermissionConstants.AnyResourceKey}' (where allowed).",
                        nameof(resourceKey));
                }

                break;

            case ResourceType.User:
            case ResourceType.Notifications:
                if (!LooksLikeEmailOrClientId(resourceKey))
                {
                    throw new ArgumentException(
                        $"{resourceType} resource key must be a user email (or a service client id).",
                        nameof(resourceKey));
                }

                break;

            default:
                break;
        }
    }

    private static bool LooksLikeEmailOrClientId(string key) =>
        key.Contains('@', StringComparison.Ordinal)
        || Guid.TryParse(key, out _);
}
