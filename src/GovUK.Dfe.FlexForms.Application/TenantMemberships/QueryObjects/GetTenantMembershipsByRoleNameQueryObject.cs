using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;

namespace GovUK.Dfe.FlexForms.Application.TenantMemberships.QueryObjects;

/// <summary>
/// Restricts memberships to a single role, matched on the role name assigned within the tenant.
/// </summary>
public sealed class GetTenantMembershipsByRoleNameQueryObject(string roleName)
    : IQueryObject<TenantMembership>
{
    private readonly string _roleName = roleName.Trim().ToLowerInvariant();

    public IQueryable<TenantMembership> Apply(IQueryable<TenantMembership> query) =>
        query.Where(m => m.Role != null && m.Role.Name.ToLower() == _roleName);
}
