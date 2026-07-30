using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.TenantMemberships.QueryObjects;

/// <summary>
/// All active memberships for a tenant, with user, role, and the user's permissions.
/// </summary>
public sealed class GetActiveTenantMembershipsWithUsersQueryObject(Guid tenantId)
    : IQueryObject<TenantMembership>
{
    public IQueryable<TenantMembership> Apply(IQueryable<TenantMembership> query) =>
        query
            .AsNoTracking()
            .Include(m => m.Role)
            .Include(m => m.User)!
                .ThenInclude(u => u!.Permissions)
            .Where(m => m.TenantId == tenantId && m.IsActive)
            .OrderBy(m => m.User!.Name);
}
