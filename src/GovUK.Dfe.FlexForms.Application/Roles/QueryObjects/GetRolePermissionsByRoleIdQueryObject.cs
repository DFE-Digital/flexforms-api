using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Application.Roles.QueryObjects;

/// <summary>
/// All permission grants attached to a tenant-scoped role.
/// </summary>
public sealed class GetRolePermissionsByRoleIdQueryObject(RoleId roleId) : IQueryObject<RolePermission>
{
    public IQueryable<RolePermission> Apply(IQueryable<RolePermission> query) =>
        query.Where(rp => rp.RoleId == roleId);
}
