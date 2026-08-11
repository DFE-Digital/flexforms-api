using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Resolves which tenant templates the current user is allowed to access.
/// Admins/template managers and Template:Any grants receive the full tenant catalogue;
/// other users receive catalogue ∩ explicit Template permission grants.
/// </summary>
public interface IUserAccessibleTemplateService
{
    /// <summary>
    /// Returns template IDs the user may access within the current tenant.
    /// </summary>
    Task<IReadOnlyList<TemplateId>> GetAccessibleTemplateIdsAsync(
        IEnumerable<Permission> permissions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves listing filters: when <paramref name="requestedTemplateId"/> is set,
    /// returns that template only if the user can access it; otherwise returns all accessible templates.
    /// </summary>
    Task<IReadOnlyList<TemplateId>> ResolveAccessibleListingFilterAsync(
        IEnumerable<Permission> permissions,
        Guid? requestedTemplateId,
        CancellationToken cancellationToken = default);
}
