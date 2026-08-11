using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Filters user-owned permission grants to those relevant to the current tenant.
/// </summary>
public interface ITenantPermissionFilter
{
    /// <summary>
    /// Returns only the permissions that belong to the current tenant catalogue.
    /// </summary>
    Task<IReadOnlyList<Permission>> FilterToCurrentTenantAsync(
        IEnumerable<Permission> permissions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true when the application's template belongs to the current tenant catalogue.
    /// </summary>
    Task<bool> ApplicationBelongsToCurrentTenantAsync(
        TemplateId templateId,
        CancellationToken cancellationToken = default);
}
