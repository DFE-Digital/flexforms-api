using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <inheritdoc />
public sealed class TenantPermissionFilter(
    ITenantTemplateCatalogue tenantTemplateCatalogue,
    IApplicationRepository applicationRepository) : ITenantPermissionFilter
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Permission>> FilterToCurrentTenantAsync(
        IEnumerable<Permission> permissions,
        CancellationToken cancellationToken = default)
    {
        var tenantTemplateIds = (await tenantTemplateCatalogue.GetTemplateIdsAsync(cancellationToken))
            .ToHashSet();

        if (tenantTemplateIds.Count == 0)
            return Array.Empty<Permission>();

        var permissionList = permissions.ToList();
        if (permissionList.Count == 0)
            return permissionList;

        var applicationTemplateMap = await BuildApplicationTemplateMapAsync(permissionList, cancellationToken);

        return permissionList
            .Where(p => BelongsToTenant(p, tenantTemplateIds, applicationTemplateMap))
            .ToList();
    }

    /// <inheritdoc />
    public Task<bool> ApplicationBelongsToCurrentTenantAsync(
        TemplateId templateId,
        CancellationToken cancellationToken = default)
        => tenantTemplateCatalogue.ContainsAsync(templateId, cancellationToken);

    internal static bool BelongsToTenant(
        Permission permission,
        HashSet<TemplateId> tenantTemplateIds,
        IReadOnlyDictionary<Guid, TemplateId> applicationTemplateMap)
    {
        switch (permission.ResourceType)
        {
            case ResourceType.Template:
                if (IsAnyKey(permission.ResourceKey))
                    return tenantTemplateIds.Count > 0;

                return Guid.TryParse(permission.ResourceKey, out var templateGuid)
                       && tenantTemplateIds.Contains(new TemplateId(templateGuid));

            case ResourceType.Application:
            case ResourceType.ApplicationFiles:
                if (IsAnyKey(permission.ResourceKey))
                    return tenantTemplateIds.Count > 0;

                if (permission.Application?.TemplateVersion?.TemplateId is { } loadedTemplateId)
                    return tenantTemplateIds.Contains(loadedTemplateId);

                if (!Guid.TryParse(permission.ResourceKey, out var applicationGuid))
                    return false;

                return applicationTemplateMap.TryGetValue(applicationGuid, out var mappedTemplateId)
                       && tenantTemplateIds.Contains(mappedTemplateId);

            case ResourceType.User:
            case ResourceType.Notifications:
            default:
                return true;
        }
    }

    private async Task<IReadOnlyDictionary<Guid, TemplateId>> BuildApplicationTemplateMapAsync(
        IReadOnlyCollection<Permission> permissions,
        CancellationToken cancellationToken)
    {
        var applicationIds = permissions
            .Where(p => p.ResourceType is ResourceType.Application or ResourceType.ApplicationFiles)
            .Where(p => !IsAnyKey(p.ResourceKey))
            .Select(p => Guid.TryParse(p.ResourceKey, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Select(id => new ApplicationId(id))
            .ToList();

        if (applicationIds.Count == 0)
            return new Dictionary<Guid, TemplateId>();

        var rows = await applicationRepository.Query()
            .AsNoTracking()
            .Where(a => applicationIds.Contains(a.Id!))
            .Select(a => new
            {
                ApplicationId = a.Id!.Value,
                TemplateId = a.TemplateVersion!.TemplateId
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            row => row.ApplicationId,
            row => row.TemplateId);
    }

    private static bool IsAnyKey(string resourceKey) =>
        string.Equals(resourceKey, PermissionConstants.AnyResourceKey, StringComparison.OrdinalIgnoreCase);
}
