using GovUK.Dfe.FlexForms.Application.TenantMemberships.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using FluentAssertions;
using MockQueryable;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryObjects.TenantMemberships;

public class GetActiveTenantMembershipsForDirectoryQueryObjectTests
{
    [Fact]
    public void Apply_ShouldReturnActiveMembershipsForTenant_OrderedByNameThenEmail()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var role = Role.CreateForTenant(tenantId, RoleNames.User, true);

        var bob = CreateUser("Bob", "bob@example.test", role.Id!);
        var aliceZ = CreateUser("Alice", "z@example.test", role.Id!);
        var aliceA = CreateUser("Alice", "a@example.test", role.Id!);

        var query = new[]
        {
            CreateMembership(tenantId, bob, role, active: true),
            CreateMembership(tenantId, aliceZ, role, active: true),
            CreateMembership(tenantId, aliceA, role, active: true),
            CreateMembership(tenantId, CreateUser("Zed", "zed@example.test", role.Id!), role, active: false),
            CreateMembership(otherTenantId, CreateUser("Other", "other@example.test", role.Id!), role, active: true)
        }.AsQueryable().BuildMock();

        var result = new GetActiveTenantMembershipsForDirectoryQueryObject(tenantId)
            .Apply(query)
            .ToList();

        result.Should().HaveCount(3);
        result.Select(m => m.User!.Email).Should().ContainInOrder("a@example.test", "z@example.test", "bob@example.test");
        result.Should().OnlyContain(m => m.IsActive && m.TenantId == tenantId);
        result.Should().OnlyContain(m => m.User != null && m.Role != null);
    }

    [Fact]
    public void Apply_ShouldFilterByUserId()
    {
        var tenantId = Guid.NewGuid();
        var role = Role.CreateForTenant(tenantId, RoleNames.User, true);
        var keep = CreateUser("Keep", "keep@example.test", role.Id!);
        var drop = CreateUser("Drop", "drop@example.test", role.Id!);

        var result = new GetActiveTenantMembershipsForDirectoryQueryObject(tenantId, keep.Id)
            .Apply(new[]
            {
                CreateMembership(tenantId, keep, role, active: true),
                CreateMembership(tenantId, drop, role, active: true)
            }.AsQueryable().BuildMock())
            .ToList();

        var membership = Assert.Single(result);
        Assert.Equal(keep.Id, membership.UserId);
    }

    [Fact]
    public void Apply_ShouldFilterByEmail_IgnoringCase()
    {
        var tenantId = Guid.NewGuid();
        var role = Role.CreateForTenant(tenantId, RoleNames.User, true);
        var keep = CreateUser("Keep", "keep@example.test", role.Id!);
        var drop = CreateUser("Drop", "drop@example.test", role.Id!);

        var result = new GetActiveTenantMembershipsForDirectoryQueryObject(tenantId, email: "KEEP@example.test")
            .Apply(new[]
            {
                CreateMembership(tenantId, keep, role, active: true),
                CreateMembership(tenantId, drop, role, active: true)
            }.AsQueryable().BuildMock())
            .ToList();

        var membership = Assert.Single(result);
        Assert.Equal("keep@example.test", membership.User!.Email);
    }

    private static User CreateUser(string name, string email, RoleId roleId)
    {
        return new User(
            new UserId(Guid.NewGuid()),
            roleId,
            name,
            email,
            DateTime.UtcNow,
            null,
            null,
            null);
    }

    private static TenantMembership CreateMembership(Guid tenantId, User user, Role role, bool active)
    {
        var membership = new TenantMembership(
            new TenantMembershipId(Guid.NewGuid()),
            tenantId,
            user.Id!,
            role.Id!,
            DateTime.UtcNow,
            isActive: active);
        membership.GetType().GetProperty(nameof(TenantMembership.User))!.SetValue(membership, user);
        membership.GetType().GetProperty(nameof(TenantMembership.Role))!.SetValue(membership, role);
        return membership;
    }
}
