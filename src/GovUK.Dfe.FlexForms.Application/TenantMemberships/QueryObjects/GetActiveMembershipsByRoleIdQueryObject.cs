using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Application.TenantMemberships.QueryObjects;

/// <summary>
/// Counts active memberships for a role (used before delete).
/// </summary>
public sealed class GetActiveMembershipsByRoleIdQueryObject(RoleId roleId) : IQueryObject<TenantMembership>
{
    public IQueryable<TenantMembership> Apply(IQueryable<TenantMembership> query) =>
        query.Where(m => m.RoleId == roleId && m.IsActive);
}
