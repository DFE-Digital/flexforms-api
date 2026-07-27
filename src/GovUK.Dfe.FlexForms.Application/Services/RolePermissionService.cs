using GovUK.Dfe.FlexForms.Application.Roles.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Seeds system-role default permissions and loads RolePermissions via Query Objects.
/// </summary>
public sealed class RolePermissionService(
    IEaRepository<RolePermission> rolePermissionRepository) : IRolePermissionService
{
    public async Task EnsureDefaultsForRoleAsync(Role role, CancellationToken cancellationToken = default)
    {
        if (role.Id is null)
            throw new ArgumentException("Role must have an Id.", nameof(role));

        var defaults = SystemRolePermissionDefaults.ForRole(role.Name);
        if (defaults.Count == 0)
            return;

        var existing = await new GetRolePermissionsByRoleIdQueryObject(role.Id)
            .Apply(rolePermissionRepository.Query())
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var grant in defaults)
        {
            var already = existing.Any(rp =>
                rp.ResourceType == grant.ResourceType
                && string.Equals(rp.ResourceKey, grant.ResourceKey, StringComparison.OrdinalIgnoreCase)
                && rp.AccessType == grant.AccessType);

            if (already)
                continue;

            var permission = new RolePermission(
                new RolePermissionId(Guid.NewGuid()),
                role.Id,
                grant.ResourceKey,
                grant.ResourceType,
                grant.AccessType,
                now);
            await rolePermissionRepository.AddAsync(permission, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<RolePermission>> GetByRoleIdAsync(
        RoleId roleId,
        CancellationToken cancellationToken = default)
    {
        return await new GetRolePermissionsByRoleIdQueryObject(roleId)
            .Apply(rolePermissionRepository.Query().AsNoTracking())
            .ToListAsync(cancellationToken);
    }
}
