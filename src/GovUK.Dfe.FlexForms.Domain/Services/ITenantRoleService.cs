using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Domain.Services;

/// <summary>
/// Application service for tenant-scoped roles (system User + custom roles).
/// Reads use Query Objects; domain mutations stay on <see cref="Entities.Role"/>.
/// Platform SuperAdmin/Admin names are reserved.
/// </summary>
public interface ITenantRoleService
{
    Task<Role> GetOrCreateTenantRoleAsync(
        Guid tenantId,
        string roleName,
        CancellationToken cancellationToken = default);

    Task EnsureSystemRolesAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Role>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<Role?> GetByIdAsync(Guid tenantId, RoleId roleId, CancellationToken cancellationToken = default);

    Task<Role?> GetByNameAsync(Guid tenantId, string roleName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a custom (non-system) tenant role. Fails if name is reserved or already exists.
    /// </summary>
    Task<Role> CreateCustomRoleAsync(
        Guid tenantId,
        string roleName,
        CancellationToken cancellationToken = default);

    Task RenameAsync(Role role, string newName, CancellationToken cancellationToken = default);

    Task DeleteAsync(Role role, CancellationToken cancellationToken = default);
}
