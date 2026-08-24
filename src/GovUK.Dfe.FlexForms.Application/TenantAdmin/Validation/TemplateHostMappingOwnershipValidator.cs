using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin.Validation;

/// <summary>
/// Ensures HostMappings / TemplateId GUIDs are claimable by the current tenant:
/// template must exist and <c>TenantId</c> is null (legacy) or matches the tenant.
/// </summary>
public interface ITemplateHostMappingOwnershipValidator
{
    Task<IReadOnlyList<string>> ValidateAsync(
        Guid tenantId,
        string category,
        string settingsJson,
        CancellationToken cancellationToken = default);
}

public sealed class TemplateHostMappingOwnershipValidator(
    IEaRepository<Template> templateRepository,
    ILogger<TemplateHostMappingOwnershipValidator> logger) : ITemplateHostMappingOwnershipValidator
{
    public async Task<IReadOnlyList<string>> ValidateAsync(
        Guid tenantId,
        string category,
        string settingsJson,
        CancellationToken cancellationToken = default)
    {
        if (!TemplateMappingSettingCategories.IsTemplateMappingCategory(category))
            return Array.Empty<string>();

        var templateIds = TemplateMappingSettingCategories.ExtractTemplateIds(settingsJson);
        if (templateIds.Count == 0)
            return Array.Empty<string>();

        var idVos = templateIds.Select(id => new TemplateId(id)).ToHashSet();
        var rows = await templateRepository.Query()
            .AsNoTracking()
            .Where(t => t.Id != null && idVos.Contains(t.Id))
            .Select(t => new { Id = t.Id!, t.TenantId })
            .ToListAsync(cancellationToken);

        var byId = rows.ToDictionary(r => r.Id.Value, r => r.TenantId);
        var errors = new List<string>();

        foreach (var id in templateIds)
        {
            if (!byId.TryGetValue(id, out var ownerTenantId))
            {
                errors.Add(
                    $"Template '{id}' was not found in the EA database and cannot be mapped.");
                continue;
            }

            if (ownerTenantId is not null && ownerTenantId.Value != tenantId)
            {
                logger.LogWarning(
                    "Rejected HostMappings/template GUID {TemplateId} for tenant {TenantId}: owned by {OwnerTenantId}",
                    id,
                    tenantId,
                    ownerTenantId);

                errors.Add(
                    $"Template '{id}' belongs to another tenant and cannot be mapped.");
            }
        }

        return errors;
    }
}
