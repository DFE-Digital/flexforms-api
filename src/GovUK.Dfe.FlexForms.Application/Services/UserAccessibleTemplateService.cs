using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <inheritdoc />
public sealed class UserAccessibleTemplateService(
    ITenantTemplateCatalogue tenantTemplateCatalogue,
    IPermissionCheckerService permissionCheckerService) : IUserAccessibleTemplateService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<TemplateId>> GetAccessibleTemplateIdsAsync(
        IEnumerable<Permission> permissions,
        CancellationToken cancellationToken = default)
    {
        var catalogue = await tenantTemplateCatalogue.GetTemplateIdsAsync(cancellationToken);
        if (catalogue.Count == 0)
            return Array.Empty<TemplateId>();

        // Admins / template managers get the full tenant catalogue (same as GetAccessibleTemplates).
        // SuperAdmin/Admin create applications via role bypass without always having per-template
        // Permission rows — without this, dashboard listing filters to zero templates.
        if (permissionCheckerService.CanManageTemplates())
            return catalogue;

        var permissionList = permissions as IList<Permission> ?? permissions.ToList();

        // Template:Any:* unlocks every template in the current tenant catalogue.
        if (permissionList.Any(p =>
                p.ResourceType == ResourceType.Template
                && string.Equals(
                    p.ResourceKey,
                    PermissionConstants.AnyResourceKey,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return catalogue;
        }

        var permitted = permissionList
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
