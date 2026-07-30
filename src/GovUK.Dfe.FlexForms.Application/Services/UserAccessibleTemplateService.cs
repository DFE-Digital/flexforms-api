using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <inheritdoc />
public sealed class UserAccessibleTemplateService(
    ITenantTemplateCatalogue tenantTemplateCatalogue) : IUserAccessibleTemplateService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<TemplateId>> GetAccessibleTemplateIdsAsync(
        IEnumerable<Permission> permissions,
        CancellationToken cancellationToken = default)
    {
        var catalogue = await tenantTemplateCatalogue.GetTemplateIdsAsync(cancellationToken);
        if (catalogue.Count == 0)
            return Array.Empty<TemplateId>();

        var permitted = permissions
            .Where(p => p.ResourceType == ResourceType.Template)
            .Select(p => Guid.TryParse(p.ResourceKey, out var id) && id != Guid.Empty
                ? new TemplateId(id)
                : null)
            .Where(id => id is not null)
            .Select(id => id!)
            .Distinct()
            .ToHashSet();

        return catalogue
            .Where(permitted.Contains)
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TemplateId>> ResolveAccessibleListingFilterAsync(
        IEnumerable<Permission> permissions,
        Guid? requestedTemplateId,
        CancellationToken cancellationToken = default)
    {
        var accessible = await GetAccessibleTemplateIdsAsync(permissions, cancellationToken);
        if (accessible.Count == 0)
            return Array.Empty<TemplateId>();

        if (!requestedTemplateId.HasValue)
            return accessible;

        var requested = new TemplateId(requestedTemplateId.Value);
        return accessible.Contains(requested)
            ? new[] { requested }
            : Array.Empty<TemplateId>();
    }
}
