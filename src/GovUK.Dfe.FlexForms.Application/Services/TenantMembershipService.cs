using GovUK.Dfe.FlexForms.Application.TenantMemberships.QueryObjects;
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
        var role = await tenantRoleService.GetOrCreateTenantRoleAsync(tenantId, roleName, cancellationToken);
        var now = DateTime.UtcNow;

        var existing = await new GetTenantMembershipForUserQueryObject(tenantId, userId)
            .Apply(membershipRepository.Query())
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            var membership = new TenantMembership(
                new TenantMembershipId(Guid.NewGuid()),
                tenantId,
                userId,
                role.Id!,
                now,
                isActive: true);
            await membershipRepository.AddAsync(membership, cancellationToken);
            return membership;
        }

        existing.AssignRole(role.Id!, now);
        existing.Activate(now);
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
