using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <inheritdoc />
public sealed class TenantPermissionFilter(
    ITenantTemplateCatalogue tenantTemplateCatalogue,
    IApplicationRepository applicationRepository,
    ITenantContextAccessor tenantContextAccessor,
    ILogger<TenantPermissionFilter> logger) : ITenantPermissionFilter
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
        {
            logger.LogWarning(
                "Denying application {ApplicationId}: tenant {TenantId} catalogue is empty.",
                applicationId,
                currentTenantId);
            return false;
        }

        var applicationIdVo = new ApplicationId(applicationId);
        // Use TemplateVersion.TemplateId only — joining Template.TenantId rejected HostMapped
        // templates owned by another tenant, so create/list worked and GET-by-reference 403'd.
        var templateId = await applicationRepository.Query()
            .AsNoTracking()
            .Where(a => a.Id == applicationIdVo)
            .Select(a => (Guid?)a.TemplateVersion!.TemplateId.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (templateId is null)
        {
            logger.LogWarning(
                "Denying application {ApplicationId}: application or template version was not found.",
                applicationId);
            return false;
        }

        var belongs = tenantTemplateIds.Contains(templateId.Value);
        if (!belongs)
        {
            logger.LogWarning(
                "Denying application {ApplicationId}: template {TemplateId} is not in tenant {TenantId} catalogue.",
                applicationId,
                templateId,
                currentTenantId);
        }

        return belongs;
    }

    /// <summary>
    /// Application/template grants belong to the current tenant when their template is in the
    /// tenant catalogue (HostMappings plus tenant-owned rows). Create and list already use
    /// that catalogue; GET must match so a HostMapped template cannot create an application
    /// that immediately 403s as "does not belong to the current tenant".
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
                       && tenantTemplateIds.Contains(templateGuid);

            case ResourceType.Application:
            case ResourceType.ApplicationFiles:
                if (IsAnyKey(permission.ResourceKey))
                    return tenantTemplateIds.Count > 0;

                if (!TryResolveApplicationId(permission, out var applicationGuid))
                    return false;

                return applicationOwnership.TryGetValue(applicationGuid, out var ownership)
                       && tenantTemplateIds.Contains(ownership.TemplateId);

            case ResourceType.Notifications:
                return TenantScopedIdentityKey.NotificationsBelongToTenant(
                    permission.ResourceKey,
                    currentTenantId);

            case ResourceType.User:
            default:
                return true;
        }
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
                TemplateId = a.TemplateVersion!.TemplateId
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            row => row.ApplicationId.Value,
            row => new ApplicationOwnership(row.TemplateId.Value));
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

    internal readonly record struct ApplicationOwnership(Guid TemplateId);
}
