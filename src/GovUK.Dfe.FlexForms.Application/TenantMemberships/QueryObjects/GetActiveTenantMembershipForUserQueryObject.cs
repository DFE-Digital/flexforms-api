using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.TenantMemberships.QueryObjects;

/// <summary>
/// Active membership for one user in one tenant, including the tenant-scoped role.
/// </summary>
public sealed class GetActiveTenantMembershipForUserQueryObject(Guid tenantId, UserId userId)
    : IQueryObject<TenantMembership>
{
    public IQueryable<TenantMembership> Apply(IQueryable<TenantMembership> query) =>
        query
            .Include(m => m.Role)
            .Where(m => m.TenantId == tenantId && m.UserId == userId && m.IsActive);
}
