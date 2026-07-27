using GovUK.Dfe.FlexForms.Application.Roles.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Application service that ensures tenant-scoped roles exist and seeds their default permissions.
/// </summary>
public sealed class TenantRoleService(
    IEaRepository<Role> roleRepository,
    IRolePermissionService rolePermissionService) : ITenantRoleService
{
    public async Task EnsureSystemRolesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        foreach (var name in RoleNames.Assignable)
        {
            await GetOrCreateTenantRoleAsync(tenantId, name, cancellationToken);
        }
    }

    public async Task<Role> GetOrCreateTenantRoleAsync(
        Guid tenantId,
        string roleName,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));

        if (string.IsNullOrWhiteSpace(roleName))
            throw new ArgumentException("Role name is required.", nameof(roleName));

        if (RoleNames.IsReservedRoleName(roleName))
        {
            throw new InvalidOperationException(
                $"Role name '{roleName.Trim()}' is reserved for platform use and cannot be used as a tenant role.");
        }

        var canonical = RoleNames.ResolveAssignable(roleName) ?? roleName.Trim();

        var existing = await new GetTenantRoleByNameQueryObject(tenantId, canonical)
            .Apply(roleRepository.Query())
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            if (existing.IsSystem)
                await rolePermissionService.EnsureDefaultsForRoleAsync(existing, cancellationToken);
            return existing;
        }

        var role = Role.CreateForTenant(tenantId, canonical, isSystem: RoleNames.IsAssignable(canonical));
        await roleRepository.AddAsync(role, cancellationToken);

        if (role.IsSystem)
            await rolePermissionService.EnsureDefaultsForRoleAsync(role, cancellationToken);

        return role;
    }
}
