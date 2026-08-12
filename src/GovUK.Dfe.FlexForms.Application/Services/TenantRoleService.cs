using GovUK.Dfe.FlexForms.Application.Roles.QueryObjects;
using GovUK.Dfe.FlexForms.Application.TenantMemberships.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Application service for tenant role lifecycle.
/// Reads use Query Objects; domain mutations stay on <see cref="Role"/>.
/// </summary>
public sealed class TenantRoleService(
    IEaRepository<Role> roleRepository,
    IEaRepository<TenantMembership> membershipRepository,
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

        var lookupName = RoleNames.ResolveAssignable(roleName) ?? roleName.Trim();
        var existing = await GetByNameAsync(tenantId, lookupName, cancellationToken);
        if (existing is not null)
        {
            if (existing.IsSystem)
                await rolePermissionService.EnsureDefaultsForRoleAsync(existing, cancellationToken);
            return existing;
        }

        var role = Role.CreateProvisionedForTenant(tenantId, roleName);
        await roleRepository.AddAsync(role, cancellationToken);

        if (role.IsSystem)
            await rolePermissionService.EnsureDefaultsForRoleAsync(role, cancellationToken);

        return role;
    }

    public async Task<IReadOnlyList<Role>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await new GetTenantRolesQueryObject(tenantId)
            .Apply(roleRepository.Query().AsNoTracking())
            .ToListAsync(cancellationToken);
    }

    public async Task<Role?> GetByIdAsync(Guid tenantId, RoleId roleId, CancellationToken cancellationToken = default)
    {
        return await new GetTenantRoleByIdQueryObject(tenantId, roleId)
            .Apply(roleRepository.Query().AsNoTracking())
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Role?> GetByNameAsync(Guid tenantId, string roleName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            return null;

        return await new GetTenantRoleByNameQueryObject(tenantId, roleName.Trim())
            .Apply(roleRepository.Query().AsNoTracking())
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Role> CreateCustomRoleAsync(
        Guid tenantId,
        string roleName,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));

        var role = Role.CreateCustomForTenant(tenantId, roleName);

        var existing = await GetByNameAsync(tenantId, role.Name, cancellationToken);
        if (existing is not null)
            throw new InvalidOperationException($"A role named '{role.Name}' already exists for this tenant.");

        await roleRepository.AddAsync(role, cancellationToken);
        return role;
    }

    public async Task RenameAsync(Role role, string newName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        if (role.Id is null)
            throw new ArgumentException("Role must have an Id.", nameof(role));

        // Domain owns rename + name policy; uniqueness needs the database.
        var pendingName = newName?.Trim() ?? throw new ArgumentNullException(nameof(newName));
        if (role.TenantId is Guid tenantId)
        {
            var clash = await GetByNameAsync(tenantId, pendingName, cancellationToken);
            if (clash is not null && clash.Id != role.Id)
                throw new InvalidOperationException($"A role named '{pendingName}' already exists for this tenant.");
        }

        var tracked = await GetTrackedByIdAsync(role, cancellationToken);
        tracked.Rename(newName);
    }

    public async Task DeleteAsync(Role role, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        role.EnsureCanBeDeleted();

        if (role.Id is null)
            throw new ArgumentException("Role must have an Id.", nameof(role));

        var hasMembers = await new GetActiveMembershipsByRoleIdQueryObject(role.Id)
            .Apply(membershipRepository.Query().AsNoTracking())
            .AnyAsync(cancellationToken);

        if (hasMembers)
        {
            throw new InvalidOperationException(
                $"Role '{role.Name}' cannot be deleted while users are assigned to it.");
        }

        var tracked = await GetTrackedByIdAsync(role, cancellationToken);
        // RolePermissions cascade-delete with the Role (EF relationship).
        await roleRepository.RemoveAsync(tracked, cancellationToken);
    }

    private async Task<Role> GetTrackedByIdAsync(Role role, CancellationToken cancellationToken)
    {
        if (role.TenantId is not Guid tenantId || role.Id is null)
            return role;

        var tracked = await new GetTenantRoleByIdQueryObject(tenantId, role.Id)
            .Apply(roleRepository.Query())
            .FirstOrDefaultAsync(cancellationToken);

        return tracked ?? role;
    }
}
