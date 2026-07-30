using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Roles.QueryObjects;

/// <summary>
/// Loads a tenant-scoped role by exact name for the given tenant.
/// </summary>
public sealed class GetTenantRoleByNameQueryObject(Guid tenantId, string roleName) : IQueryObject<Role>
{
    private readonly string _roleName = roleName.Trim();

    public IQueryable<Role> Apply(IQueryable<Role> query) =>
        query.Where(r => r.TenantId == tenantId && r.Name == _roleName);
}
