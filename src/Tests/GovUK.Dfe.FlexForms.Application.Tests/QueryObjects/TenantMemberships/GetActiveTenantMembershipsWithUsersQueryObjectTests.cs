using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.TenantMemberships.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using FluentAssertions;
using MockQueryable;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryObjects.TenantMemberships;

public class GetActiveTenantMembershipsWithUsersQueryObjectTests
{
    [Fact]
    public void Apply_ShouldReturnActiveMemberships_OrderedByUserName_WithIncludes()
    {
        var tenantId = Guid.NewGuid();
        var role = Role.CreateForTenant(tenantId, RoleNames.User, true);

        var bob = CreateUser("Bob", role.Id!);
        var alice = CreateUser("Alice", role.Id!);

        var bobMembership = CreateMembership(tenantId, bob, role, active: true);
        var aliceMembership = CreateMembership(tenantId, alice, role, active: true);
        var inactive = CreateMembership(tenantId, CreateUser("Zed", role.Id!), role, active: false);

        var result = new GetActiveTenantMembershipsWithUsersQueryObject(tenantId)
            .Apply(new[] { bobMembership, aliceMembership, inactive }.AsQueryable().BuildMock())
            .ToList();

        result.Should().HaveCount(2);
        result.Select(m => m.User!.Name).Should().ContainInOrder("Alice", "Bob");
        result.Should().OnlyContain(m => m.IsActive);
        result.Should().OnlyContain(m => m.Role != null);
        result.Should().OnlyContain(m => m.User != null);
        result.Should().OnlyContain(m => m.User!.Permissions != null);
    }

    private static User CreateUser(string name, RoleId roleId)
    {
        var userId = new UserId(Guid.NewGuid());
        var user = new User(
            userId,
            roleId,
            name,
            $"{name.ToLowerInvariant()}@example.com",
            DateTime.UtcNow,
            null,
            null,
            null,
            initialPermissions:
            [
                new Permission(
                    new PermissionId(Guid.NewGuid()),
                    userId,
                    applicationId: null,
                    Guid.NewGuid().ToString(),
                    ResourceType.Template,
                    AccessType.Read,
                    DateTime.UtcNow,
                    userId)
            ]);
        return user;
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
