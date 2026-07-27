using GovUK.Dfe.FlexForms.Application.TenantMemberships.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using FluentAssertions;
using MockQueryable;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryObjects.TenantMemberships;

public class GetActiveTenantMembershipForUserQueryObjectTests
{
    [Fact]
    public void Apply_ShouldReturnActiveMembership_WithRole()
    {
        var tenantId = Guid.NewGuid();
        var userId = new UserId(Guid.NewGuid());
        var role = Role.CreateForTenant(tenantId, RoleNames.SuperAdmin, true);
        var active = new TenantMembership(
            new TenantMembershipId(Guid.NewGuid()),
            tenantId,
            userId,
            role.Id!,
            DateTime.UtcNow);
        active.GetType().GetProperty(nameof(TenantMembership.Role))!.SetValue(active, role);

        var inactive = new TenantMembership(
            new TenantMembershipId(Guid.NewGuid()),
            tenantId,
            userId,
            role.Id!,
            DateTime.UtcNow,
            isActive: false);

        var otherUser = new TenantMembership(
            new TenantMembershipId(Guid.NewGuid()),
            tenantId,
            new UserId(Guid.NewGuid()),
            role.Id!,
            DateTime.UtcNow);

        var result = new GetActiveTenantMembershipForUserQueryObject(tenantId, userId)
            .Apply(new[] { active, inactive, otherUser }.AsQueryable().BuildMock())
            .ToList();

        result.Should().ContainSingle();
        result[0].IsActive.Should().BeTrue();
        result[0].Role.Should().NotBeNull();
        result[0].Role!.Name.Should().Be(RoleNames.SuperAdmin);
    }

    [Fact]
    public void Apply_ShouldReturnEmpty_WhenOnlyInactiveExists()
    {
        var tenantId = Guid.NewGuid();
        var userId = new UserId(Guid.NewGuid());
        var role = Role.CreateForTenant(tenantId, RoleNames.User, true);
        var inactive = new TenantMembership(
            new TenantMembershipId(Guid.NewGuid()),
            tenantId,
            userId,
            role.Id!,
            DateTime.UtcNow,
            isActive: false);

        var result = new GetActiveTenantMembershipForUserQueryObject(tenantId, userId)
            .Apply(new[] { inactive }.AsQueryable().BuildMock())
            .ToList();

        result.Should().BeEmpty();
    }
}
