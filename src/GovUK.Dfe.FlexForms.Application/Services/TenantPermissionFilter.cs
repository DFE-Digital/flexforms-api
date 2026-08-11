using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <inheritdoc />
public sealed class TenantPermissionFilter(
    ITenantTemplateCatalogue tenantTemplateCatalogue,
    IApplicationRepository applicationRepository,
    ITenantContextAccessor tenantContextAccessor) : ITenantPermissionFilter
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Permission>> FilterToCurrentTenantAsync(
        IEnumerable<Permission> permissions,
        CancellationToken cancellationToken = default)
    {
        var currentTenantId = tenantContextAccessor.CurrentTenant?.Id;
        if (currentTenantId is null)
            return Array.Empty<Permission>();

        var tenantTemplateIds = (await tenantTemplateCatalogue.GetTemplateIdsAsync(cancellationToken))
            .Select(id => id.Value)
            .ToHashSet();

        if (tenantTemplateIds.Count == 0)
            return Array.Empty<Permission>();

        var permissionList = permissions.ToList();
        if (permissionList.Count == 0)
            return permissionList;

        var applicationOwnership = await BuildApplicationOwnershipMapAsync(
            permissionList,
            cancellationToken);

        return permissionList
            .Where(p => BelongsToTenant(
                p,
                currentTenantId.Value,
                tenantTemplateIds,
                applicationOwnership))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<bool> ApplicationBelongsToCurrentTenantAsync(
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        var currentTenantId = tenantContextAccessor.CurrentTenant?.Id;
        if (currentTenantId is null || applicationId == Guid.Empty)
            return false;

        var tenantTemplateIds = (await tenantTemplateCatalogue.GetTemplateIdsAsync(cancellationToken))
            .Select(id => id.Value)
            .ToHashSet();

        if (tenantTemplateIds.Count == 0)
            return false;

        var applicationIdVo = new ApplicationId(applicationId);
        var ownership = await applicationRepository.Query()
            .AsNoTracking()
            .Where(a => a.Id == applicationIdVo)
            .Select(a => new
            {
                TemplateId = a.TemplateVersion!.TemplateId,
                TemplateTenantId = a.TemplateVersion!.Template!.TenantId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (ownership is null)
            return false;

        return IsTemplateInTenant(
            ownership.TemplateId.Value,
            ownership.TemplateTenantId,
            currentTenantId.Value,
            tenantTemplateIds);
    }

    /// <summary>
    /// Application/template grants belong to the current tenant when their template is in the
    /// tenant catalogue and, when the template is tenant-owned, the owning tenant matches.
    /// This prevents HostMappings overlap from leaking another tenant's application grants
    /// into the permissions list.
    /// </summary>
    internal static bool BelongsToTenant(
        Permission permission,
        Guid currentTenantId,
        HashSet<Guid> tenantTemplateIds,
        IReadOnlyDictionary<Guid, ApplicationOwnership> applicationOwnership)
    {
        switch (permission.ResourceType)
        {
            case ResourceType.Template:
                if (IsAnyKey(permission.ResourceKey))
                    return tenantTemplateIds.Count > 0;

                return Guid.TryParse(permission.ResourceKey, out var templateGuid)
                       && IsTemplateInTenant(templateGuid, owningTenantId: null, currentTenantId, tenantTemplateIds);

            case ResourceType.Application:
            case ResourceType.ApplicationFiles:
                if (IsAnyKey(permission.ResourceKey))
                    return tenantTemplateIds.Count > 0;

                if (!TryResolveApplicationId(permission, out var applicationGuid))
                    return false;

                if (!applicationOwnership.TryGetValue(applicationGuid, out var ownership))
                    return false;

                return IsTemplateInTenant(
                    ownership.TemplateId,
                    ownership.TemplateTenantId,
                    currentTenantId,
                    tenantTemplateIds);

            case ResourceType.User:
            case ResourceType.Notifications:
            default:
                return true;
        }
    }

    internal static bool IsTemplateInTenant(
        Guid templateId,
        Guid? owningTenantId,
        Guid currentTenantId,
        HashSet<Guid> tenantTemplateIds)
    {
        if (!tenantTemplateIds.Contains(templateId))
            return false;

        // Tenant-owned templates are authoritative: never treat another tenant's owned
        // template as belonging here just because it also appears in HostMappings.
        if (owningTenantId is Guid owner && owner != currentTenantId)
            return false;

        return true;
    }

    private async Task<IReadOnlyDictionary<Guid, ApplicationOwnership>> BuildApplicationOwnershipMapAsync(
        IReadOnlyCollection<Permission> permissions,
        CancellationToken cancellationToken)
    {
        // Use ApplicationId value objects (not Guid/.Value) so EF can translate Contains.
        var applicationIds = permissions
            .Where(p => p.ResourceType is ResourceType.Application or ResourceType.ApplicationFiles)
            .Select(p => TryResolveApplicationId(p, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Select(id => new ApplicationId(id))
            .ToHashSet();

        if (applicationIds.Count == 0)
            return new Dictionary<Guid, ApplicationOwnership>();

        var rows = await applicationRepository.Query()
            .AsNoTracking()
            .Where(a => a.Id != null && applicationIds.Contains(a.Id))
            .Select(a => new
            {
                ApplicationId = a.Id!,
                TemplateId = a.TemplateVersion!.TemplateId,
                TemplateTenantId = a.TemplateVersion!.Template!.TenantId
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            row => row.ApplicationId.Value,
            row => new ApplicationOwnership(row.TemplateId.Value, row.TemplateTenantId));
    }

    private static bool TryResolveApplicationId(Permission permission, out Guid applicationId)
    {
        if (permission.ApplicationId is not null)
        {
            applicationId = permission.ApplicationId.Value;
            return applicationId != Guid.Empty;
        }

        return Guid.TryParse(permission.ResourceKey, out applicationId) && applicationId != Guid.Empty;
    }

    private static bool IsAnyKey(string resourceKey) =>
        string.Equals(resourceKey, PermissionConstants.AnyResourceKey, StringComparison.OrdinalIgnoreCase);

    internal readonly record struct ApplicationOwnership(Guid TemplateId, Guid? TemplateTenantId);
}
