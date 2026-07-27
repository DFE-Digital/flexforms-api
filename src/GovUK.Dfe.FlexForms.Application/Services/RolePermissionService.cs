using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Application service for RolePermission persistence via the <see cref="Role"/> aggregate.
/// Domain policy lives on <see cref="Role"/>; children are not aggregate roots and cannot use
/// <see cref="IEaRepository{T}"/> (requires <c>IAggregateRoot</c>).
/// </summary>
public sealed class RolePermissionService(
    IEaRepository<Role> roleRepository) : IRolePermissionService
{
    public async Task EnsureDefaultsForRoleAsync(Role role, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        if (role.Id is null)
            throw new ArgumentException("Role must have an Id.", nameof(role));

        var defaults = SystemRolePermissionDefaults.ForRole(role.Name);
        if (defaults.Count == 0)
            return;

        var tracked = await GetTrackedRoleWithPermissionsAsync(role, cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var grant in defaults)
        {
            var already = tracked.Permissions.Any(rp =>
                rp.ResourceType == grant.ResourceType
                && string.Equals(rp.ResourceKey, grant.ResourceKey, StringComparison.OrdinalIgnoreCase)
                && rp.AccessType == grant.AccessType);

            if (already)
                continue;

            tracked.CreatePermission(
                grant.ResourceKey,
                grant.ResourceType,
                grant.AccessType,
                now);
        }
    }

    public async Task<IReadOnlyList<RolePermission>> GetByRoleIdAsync(
        RoleId roleId,
        CancellationToken cancellationToken = default)
    {
        return await roleRepository.Query().AsNoTracking()
            .Where(r => r.Id == roleId)
            .SelectMany(r => r.Permissions)
            .ToListAsync(cancellationToken);
    }

    public async Task ReplacePermissionsAsync(
        Role role,
        IReadOnlyCollection<(ResourceType ResourceType, string ResourceKey, AccessType AccessType)> grants,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        if (role.Id is null)
            throw new ArgumentException("Role must have an Id.", nameof(role));

        var tracked = await GetTrackedRoleWithPermissionsAsync(role, cancellationToken);
        tracked.BuildReplacedPermissions(grants, DateTime.UtcNow);
    }

    private async Task<Role> GetTrackedRoleWithPermissionsAsync(Role role, CancellationToken cancellationToken)
    {
        var tracked = await roleRepository.Query()
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.Id == role.Id, cancellationToken);

        // Newly added roles may not be queryable yet; mutate the provided instance.
        return tracked ?? role;
    }
}
