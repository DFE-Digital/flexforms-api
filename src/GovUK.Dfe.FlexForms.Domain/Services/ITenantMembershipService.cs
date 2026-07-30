using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Domain.Services;

/// <summary>
/// Manages per-tenant user memberships in a shared EA database.
/// </summary>
public interface ITenantMembershipService
{
    /// <summary>
    /// Returns the active membership for the user in the tenant, or null.
    /// </summary>
    Task<TenantMembership?> GetActiveMembershipAsync(
        Guid tenantId,
        UserId userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates the user's membership for the tenant with the given role name.
    /// </summary>
    Task<TenantMembership> UpsertMembershipAsync(
        Guid tenantId,
        UserId userId,
        string roleName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates the user's membership for the tenant (if any).
    /// </summary>
    Task DeactivateMembershipAsync(
        Guid tenantId,
        UserId userId,
        CancellationToken cancellationToken = default);
}
