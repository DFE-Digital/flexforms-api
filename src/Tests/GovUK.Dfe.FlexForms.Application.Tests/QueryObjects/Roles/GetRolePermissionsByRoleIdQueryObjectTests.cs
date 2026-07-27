using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Roles.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using FluentAssertions;
using MockQueryable;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryObjects.Roles;

public class GetRolePermissionsByRoleIdQueryObjectTests
{
    [Fact]
    public void Apply_ShouldReturnPermissionsForRole()
    {
        var roleId = new RoleId(Guid.NewGuid());
        var otherRoleId = new RoleId(Guid.NewGuid());
        var rows = new List<RolePermission>
        {
            new(new RolePermissionId(Guid.NewGuid()), roleId, "Any", ResourceType.Application, AccessType.Read, DateTime.UtcNow),
            new(new RolePermissionId(Guid.NewGuid()), roleId, "Any", ResourceType.ApplicationFiles, AccessType.Read, DateTime.UtcNow),
            new(new RolePermissionId(Guid.NewGuid()), otherRoleId, "Any", ResourceType.Application, AccessType.Write, DateTime.UtcNow)
        };

        var result = new GetRolePermissionsByRoleIdQueryObject(roleId)
            .Apply(rows.AsQueryable().BuildMock())
            .ToList();

        result.Should().HaveCount(2);
        result.Should().OnlyContain(rp => rp.RoleId == roleId);
    }
}
