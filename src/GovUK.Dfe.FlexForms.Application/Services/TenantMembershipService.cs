using GovUK.Dfe.FlexForms.Application.TenantMemberships.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Application service for tenant membership lifecycle.
/// Reads use Query Objects; domain mutations stay on <see cref="TenantMembership"/>.
/// </summary>
public sealed class TenantMembershipService(
    IEaRepository<TenantMembership> membershipRepository,
    ITenantRoleService tenantRoleService) : ITenantMembershipService
{
    public async Task<TenantMembership?> GetActiveMembershipAsync(
        Guid tenantId,
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        return await new GetActiveTenantMembershipForUserQueryObject(tenantId, userId)
            .Apply(membershipRepository.Query())
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<TenantMembership> UpsertMembershipAsync(
        Guid tenantId,
        UserId userId,
        string roleName,
        CancellationToken cancellationToken = default)
    {
        // First access / membership for a tenant often only asks for User — still seed Admin+User.
        await tenantRoleService.EnsureSystemRolesAsync(tenantId, cancellationToken);

        var role = await tenantRoleService.GetOrCreateTenantRoleAsync(tenantId, roleName, cancellationToken);
        var now = DateTime.UtcNow;

        var existing = await new GetTenantMembershipForUserQueryObject(tenantId, userId)
            .Apply(membershipRepository.Query())
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            var membership = string.Equals(roleName, RoleNames.User, StringComparison.OrdinalIgnoreCase)
                ? TenantMembership.CreateSelfRegisteredUser(tenantId, userId, role.Id!, now)
                : TenantMembership.Create(tenantId, userId, role.Id!, now);

            await membershipRepository.AddAsync(membership, cancellationToken);
            return membership;
        }

        existing.ReassignAndActivate(role.Id!, now);
        return existing;
    }

    public async Task DeactivateMembershipAsync(
        Guid tenantId,
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        var existing = await new GetTenantMembershipForUserQueryObject(tenantId, userId)
            .Apply(membershipRepository.Query())
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is null)
            return;

        existing.Deactivate(DateTime.UtcNow);
    }
}
