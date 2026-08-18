using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Domain.Services;

/// <summary>
/// Helpers for reading template (form) access from the unified <see cref="Permission"/> store
/// (<see cref="ResourceType.Template"/> grants).
/// </summary>
public static class UserTemplateAccess
{
    public static IEnumerable<Permission> GetTemplateGrants(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.Permissions.Where(p => p.ResourceType == ResourceType.Template);
    }

    public static IReadOnlyList<TemplateId> GetTemplateIds(User user)
    {
        return GetTemplateGrants(user)
            .Select(TryParseTemplateId)
            .Where(id => id is not null)
            .Select(id => id!)
            .Distinct()
            .ToList();
    }

    public static bool HasAccess(User user, TemplateId templateId)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(templateId);

        var key = templateId.Value.ToString();
        return user.Permissions.Any(p =>
            p.ResourceType == ResourceType.Template
            && string.Equals(p.ResourceKey, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// True when the user can start applications on this template
    /// (<see cref="AccessType.Write"/> on the template or tenant-wide Any).
    /// </summary>
    public static bool HasWrite(User user, TemplateId templateId)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(templateId);

        var key = templateId.Value.ToString();
        return user.Permissions.Any(p =>
            p.ResourceType == ResourceType.Template
            && p.AccessType == AccessType.Write
            && (IsAnyKey(p.ResourceKey)
                || string.Equals(p.ResourceKey, key, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// True when the user can start applications on at least one form in this tenant.
    /// Template:Write on other tenants is ignored so an invite here cannot be treated as
    /// full membership of this tenant.
    /// </summary>
    public static bool HasWriteOnTenant(User user, IReadOnlySet<Guid> tenantTemplateIds)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(tenantTemplateIds);

        return user.Permissions.Any(p =>
            p.ResourceType == ResourceType.Template
            && p.AccessType == AccessType.Write
            && IsTenantTemplateKey(p.ResourceKey, tenantTemplateIds));
    }

    /// <summary>
    /// True when, on this tenant, the user was invited onto specific applications and cannot
    /// start new ones (has template Read and application grants here, but no Template:Write
    /// on this tenant). Existing members who already have Write on any form in this tenant
    /// are not invite-only — inviting them must not take away that access.
    /// </summary>
    public static bool IsApplicationInviteOnly(User user, IReadOnlySet<Guid> tenantTemplateIds)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(tenantTemplateIds);

        if (HasWriteOnTenant(user, tenantTemplateIds))
            return false;

        var hasReadOnTenant = user.Permissions.Any(p =>
            p.ResourceType == ResourceType.Template
            && p.AccessType == AccessType.Read
            && IsTenantTemplateKey(p.ResourceKey, tenantTemplateIds));

        if (!hasReadOnTenant)
            return false;

        return user.Permissions.Any(p =>
            p.ResourceType == ResourceType.Application
            && p.ApplicationId is not null);
    }

    private static bool IsTenantTemplateKey(string resourceKey, IReadOnlySet<Guid> tenantTemplateIds)
    {
        if (IsAnyKey(resourceKey))
            return tenantTemplateIds.Count > 0;

        return Guid.TryParse(resourceKey, out var id) && tenantTemplateIds.Contains(id);
    }

    private static bool IsAnyKey(string resourceKey) =>
        string.Equals(resourceKey, PermissionConstants.AnyResourceKey, StringComparison.OrdinalIgnoreCase);

    private static TemplateId? TryParseTemplateId(Permission permission)
    {
        if (!Guid.TryParse(permission.ResourceKey, out var id) || id == Guid.Empty)
            return null;

        return new TemplateId(id);
    }
}
