using GovUK.Dfe.FlexForms.Domain.Entities;

namespace GovUK.Dfe.FlexForms.Domain.Services;

/// <summary>
/// Ensures and resolves tenant-scoped roles (including the system User role).
/// Platform SuperAdmin/Admin names are reserved and must not be created here.
/// </summary>
public interface ITenantRoleService
{
    /// <summary>
    /// Returns the tenant-scoped role with the given name, creating a system role when missing
    /// for the well-known tenant-assignable name (User), or a custom role otherwise.
    /// Reserved platform names (SuperAdmin/Admin) are rejected.
    /// </summary>
    Task<Role> GetOrCreateTenantRoleAsync(
        Guid tenantId,
        string roleName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures tenant-assignable system roles (User) exist for the tenant.
    /// </summary>
    Task EnsureSystemRolesAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
