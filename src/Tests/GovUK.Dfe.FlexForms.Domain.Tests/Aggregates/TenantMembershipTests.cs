using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Domain.Tests.Aggregates;

public class TenantMembershipTests
{
    [Fact]
    public void CreateSelfRegisteredUser_CreatesActiveMembership()
    {
        var tenantId = Guid.NewGuid();
        var userId = new UserId(Guid.NewGuid());
        var roleId = new RoleId(Guid.NewGuid());
        var when = DateTime.UtcNow;

        var membership = TenantMembership.CreateSelfRegisteredUser(tenantId, userId, roleId, when);

        Assert.Equal(tenantId, membership.TenantId);
        Assert.Equal(userId, membership.UserId);
        Assert.Equal(roleId, membership.RoleId);
        Assert.True(membership.IsActive);
        Assert.Equal(when, membership.CreatedOn);
        Assert.NotNull(membership.Id);
    }

    [Fact]
    public void ReassignAndActivate_UpdatesRoleAndActivates()
    {
        var membership = TenantMembership.Create(
            Guid.NewGuid(),
            new UserId(Guid.NewGuid()),
            new RoleId(Guid.NewGuid()),
            DateTime.UtcNow);

        membership.Deactivate(DateTime.UtcNow);
        var newRole = new RoleId(Guid.NewGuid());
        var when = DateTime.UtcNow.AddMinutes(1);

        membership.ReassignAndActivate(newRole, when);

        Assert.Equal(newRole, membership.RoleId);
        Assert.True(membership.IsActive);
        Assert.Equal(when, membership.LastModifiedOn);
    }
}
