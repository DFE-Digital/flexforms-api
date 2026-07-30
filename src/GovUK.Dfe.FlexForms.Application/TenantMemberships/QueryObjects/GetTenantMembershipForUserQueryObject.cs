using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.TenantMemberships.QueryObjects;

/// <summary>
/// Membership row for one user in one tenant (active or inactive), including role.
/// Used when upserting / reactivating membership.
/// </summary>
public sealed class GetTenantMembershipForUserQueryObject(Guid tenantId, UserId userId)
    : IQueryObject<TenantMembership>
{
    public IQueryable<TenantMembership> Apply(IQueryable<TenantMembership> query) =>
        query
            .Include(m => m.Role)
            .Where(m => m.TenantId == tenantId && m.UserId == userId);
}
