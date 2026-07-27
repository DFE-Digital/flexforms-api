using GovUK.Dfe.FlexForms.Application.Roles.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using FluentAssertions;
using MockQueryable;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryObjects.Roles;

public class GetTenantRoleByNameQueryObjectTests
{
    [Fact]
    public void Apply_ShouldReturnMatchingTenantRole()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var roles = new List<Role>
        {
            Role.CreateForTenant(tenantId, RoleNames.SuperAdmin, isSystem: true),
            Role.CreateForTenant(tenantId, RoleNames.User, isSystem: true),
            Role.CreateForTenant(otherTenantId, RoleNames.SuperAdmin, isSystem: true),
            new Role(new RoleId(Guid.NewGuid()), RoleNames.SuperAdmin)
        };

        var result = new GetTenantRoleByNameQueryObject(tenantId, RoleNames.SuperAdmin)
            .Apply(roles.AsQueryable().BuildMock())
            .ToList();

        result.Should().ContainSingle();
        result[0].TenantId.Should().Be(tenantId);
        result[0].Name.Should().Be(RoleNames.SuperAdmin);
    }

    [Fact]
    public void Apply_ShouldReturnEmpty_WhenNameDoesNotMatch()
    {
        var tenantId = Guid.NewGuid();
        var roles = new List<Role>
        {
            Role.CreateForTenant(tenantId, RoleNames.User, isSystem: true)
        };

        var result = new GetTenantRoleByNameQueryObject(tenantId, RoleNames.SuperAdmin)
            .Apply(roles.AsQueryable().BuildMock())
            .ToList();

        result.Should().BeEmpty();
    }
}
