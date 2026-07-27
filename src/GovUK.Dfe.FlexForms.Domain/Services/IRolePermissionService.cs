using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Domain.Services;

/// <summary>
/// Seeds and maintains default <see cref="RolePermission"/> rows for system roles.
/// </summary>
public interface IRolePermissionService
{
    /// <summary>
    /// Ensures default RolePermissions exist for a system role (idempotent).
    /// </summary>
    Task EnsureDefaultsForRoleAsync(Role role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads all RolePermissions for the given role.
    /// </summary>
    Task<IReadOnlyList<RolePermission>> GetByRoleIdAsync(
        RoleId roleId,
        CancellationToken cancellationToken = default);
}
