using GovUK.Dfe.FlexForms.Application.TenantMemberships.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using FluentAssertions;
using MockQueryable;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryObjects.TenantMemberships;

public class GetTenantMembershipForUserQueryObjectTests
{
    [Fact]
    public void Apply_ShouldReturnMembership_EvenWhenInactive()
    {
        var tenantId = Guid.NewGuid();
        var userId = new UserId(Guid.NewGuid());
        var role = Role.CreateForTenant(tenantId, RoleNames.Caseworker, true);
        var inactive = new TenantMembership(
            new TenantMembershipId(Guid.NewGuid()),
            tenantId,
            userId,
            role.Id!,
            DateTime.UtcNow,
            isActive: false);
        inactive.GetType().GetProperty(nameof(TenantMembership.Role))!.SetValue(inactive, role);

        var result = new GetTenantMembershipForUserQueryObject(tenantId, userId)
            .Apply(new[] { inactive }.AsQueryable().BuildMock())
            .ToList();

        result.Should().ContainSingle();
        result[0].IsActive.Should().BeFalse();
        result[0].Role.Should().NotBeNull();
    }

    [Fact]
    public void Apply_ShouldNotReturnOtherTenantMembership()
    {
        var tenantId = Guid.NewGuid();
        var userId = new UserId(Guid.NewGuid());
        var role = Role.CreateForTenant(tenantId, RoleNames.User, true);
        var other = new TenantMembership(
            new TenantMembershipId(Guid.NewGuid()),
            Guid.NewGuid(),
            userId,
            role.Id!,
            DateTime.UtcNow);

        var result = new GetTenantMembershipForUserQueryObject(tenantId, userId)
            .Apply(new[] { other }.AsQueryable().BuildMock())
            .ToList();

        result.Should().BeEmpty();
    }
}
