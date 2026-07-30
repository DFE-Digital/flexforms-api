using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Application.Roles.QueryObjects;

/// <summary>
/// Loads a tenant-scoped role by id.
/// </summary>
public sealed class GetTenantRoleByIdQueryObject(Guid tenantId, RoleId roleId) : IQueryObject<Role>
{
    public IQueryable<Role> Apply(IQueryable<Role> query) =>
        query.Where(r => r.TenantId == tenantId && r.Id == roleId);
}
