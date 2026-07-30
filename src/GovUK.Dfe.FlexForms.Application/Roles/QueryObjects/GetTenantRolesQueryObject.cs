using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;

namespace GovUK.Dfe.FlexForms.Application.Roles.QueryObjects;

/// <summary>
/// Lists all roles scoped to a tenant.
/// </summary>
public sealed class GetTenantRolesQueryObject(Guid tenantId) : IQueryObject<Role>
{
    public IQueryable<Role> Apply(IQueryable<Role> query) =>
        query.Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.Name);
}
