using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <inheritdoc />
public sealed class TenantTemplateCatalogue(
    IEaRepository<Template> templateRepository,
    ITenantContextAccessor tenantContextAccessor,
    ILogger<TenantTemplateCatalogue> logger) : ITenantTemplateCatalogue
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<TemplateId>> GetTemplateIdsAsync(CancellationToken cancellationToken = default)
    {
        var tenant = tenantContextAccessor.CurrentTenant;
        if (tenant is null)
        {
            logger.LogWarning("No current tenant when resolving tenant template catalogue.");
            return Array.Empty<TemplateId>();
        }

        // Configured mappings are claimable only when the template exists and is legacy
        // (TenantId null) or owned by this tenant. Foreign TenantId GUIDs are ignored.
        var fromConfig = ReadConfiguredTemplateIds(tenant);
        var claimableFromConfig = await FilterClaimableConfiguredIdsAsync(
            fromConfig,
            tenant.Id,
            tenant.Name,
            cancellationToken);

        var ownedTemplateIds = await templateRepository.Query()
            .AsNoTracking()
            .Where(template => template.TenantId == tenant.Id && template.Id != null)
            .Select(template => template.Id!)
            .ToListAsync(cancellationToken);

        var tenantTemplateIds = claimableFromConfig
            .Concat(ownedTemplateIds)
            .Distinct()
            .ToList()
            .AsReadOnly();

        logger.LogDebug(
            "Tenant {TenantName} catalogue resolved from claimable mappings and owned templates ({Count} template(s)).",
            tenant.Name,
            tenantTemplateIds.Count);

        return tenantTemplateIds;
    }

    /// <inheritdoc />
    public async Task<bool> ContainsAsync(TemplateId templateId, CancellationToken cancellationToken = default)
    {
        var catalogue = await GetTemplateIdsAsync(cancellationToken);
        return catalogue.Any(t => t == templateId);
    }

    private async Task<IReadOnlyList<TemplateId>> FilterClaimableConfiguredIdsAsync(
        IReadOnlyList<TemplateId> configuredIds,
        Guid tenantId,
        string tenantName,
        CancellationToken cancellationToken)
    {
        if (configuredIds.Count == 0)
            return Array.Empty<TemplateId>();

        var idSet = configuredIds.ToHashSet();
        var rows = await templateRepository.Query()
            .AsNoTracking()
            .Where(t => t.Id != null && idSet.Contains(t.Id))
            .Select(t => new { Id = t.Id!, t.TenantId })
            .ToListAsync(cancellationToken);

        var byId = rows.ToDictionary(r => r.Id, r => r.TenantId);
        var claimable = new List<TemplateId>();

        foreach (var id in configuredIds)
        {
            if (!byId.TryGetValue(id, out var ownerTenantId))
            {
                logger.LogWarning(
                    "Ignoring HostMappings/template GUID {TemplateId} for tenant {TenantName}: template not found in EA database.",
                    id.Value,
                    tenantName);
                continue;
            }

            if (ownerTenantId is not null && ownerTenantId.Value != tenantId)
            {
                logger.LogWarning(
                    "Ignoring HostMappings/template GUID {TemplateId} for tenant {TenantName}: owned by another tenant ({OwnerTenantId}).",
                    id.Value,
                    tenantName,
                    ownerTenantId);
                continue;
            }

            claimable.Add(id);
        }

        return claimable;
    }

    private IReadOnlyList<TemplateId> ReadConfiguredTemplateIds(TenantConfiguration tenant)
    {
        var templateIds = new List<TemplateId>();

        AddMappedIds(
            tenant,
            templateIds,
            "ApplicationTemplates:HostMappings",
            "ApplicationTemplates:HostMappings");
        AddMappedIds(
            tenant,
            templateIds,
            "Template:HostMappings",
            "Template:HostMappings");

        AddSingleId(tenant, templateIds, "ApplicationTemplates:TemplateId");
        AddSingleId(tenant, templateIds, "Template:Id");

        return templateIds
            .Distinct()
            .ToList()
            .AsReadOnly();
    }

    private void AddMappedIds(
        TenantConfiguration tenant,
        List<TemplateId> templateIds,
        string sectionPath,
        string logLabel)
    {
        foreach (var child in tenant.Settings.GetSection(sectionPath).GetChildren())
        {
            var value = child.Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (Guid.TryParse(value, out var templateGuid))
            {
                templateIds.Add(new TemplateId(templateGuid));
                continue;
            }

            logger.LogWarning(
                "Ignoring invalid template GUID in {Section} for tenant {TenantName}. RawValue={RawValue}",
                logLabel,
                tenant.Name,
                value);
        }
    }

    private void AddSingleId(
        TenantConfiguration tenant,
        List<TemplateId> templateIds,
        string key)
    {
        var value = tenant.Settings[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (Guid.TryParse(value, out var templateGuid))
        {
            templateIds.Add(new TemplateId(templateGuid));
            return;
        }

        logger.LogWarning(
            "Ignoring invalid template GUID in {Key} for tenant {TenantName}. RawValue={RawValue}",
            key,
            tenant.Name,
            value);
    }
}
