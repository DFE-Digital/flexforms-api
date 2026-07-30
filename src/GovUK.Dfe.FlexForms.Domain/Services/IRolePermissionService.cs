using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;

namespace GovUK.Dfe.FlexForms.Domain.Services;

/// <summary>
/// Persists <see cref="RolePermission"/> rows. Domain policy lives on <see cref="Entities.Role"/>.
/// </summary>
public interface IRolePermissionService
{
    Task EnsureDefaultsForRoleAsync(Role role, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RolePermission>> GetByRoleIdAsync(
        RoleId roleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces all permissions for the role with the provided grants.
    /// </summary>
    Task ReplacePermissionsAsync(
        Role role,
        IReadOnlyCollection<(ResourceType ResourceType, string ResourceKey, AccessType AccessType)> grants,
        CancellationToken cancellationToken = default);
}
