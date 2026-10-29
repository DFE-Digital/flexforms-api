using GovUK.Dfe.FlexForms.Application.Applications;
using GovUK.Dfe.FlexForms.Domain.Common;

namespace GovUK.Dfe.FlexForms.Application.Tests.Applications;

public class ContributorInviteAccessTests
{
    [Fact]
    public void TryReadRoleId_IsFalse_WhenTheTemplateDoesNotChooseARole()
    {
        Assert.False(ContributorInviteAccess.TryReadRoleId("{}", out _));
        Assert.False(ContributorInviteAccess.TryReadRoleId(null, out _));
        Assert.Equal(
            ContributorRoleSelection.NotSelected,
            ContributorInviteAccess.ReadRoleId("""{"contributorRoleId":""}""", out _));
    }

    [Theory]
    [InlineData("""{"contributorRoleId":"not-a-guid"}""")]
    [InlineData("""{"contributorRoleId":42}""")]
    public void ReadRoleId_IsInvalid_WhenTheTemplateConfiguresAnInvalidRole(string schema)
    {
        Assert.Equal(
            ContributorRoleSelection.Invalid,
            ContributorInviteAccess.ReadRoleId(schema, out _));
    }

    [Fact]
    public void Decide_UserRole_KeepsEditAccess()
    {
        var decision = ContributorInviteAccess.Decide(RoleConstants.UserRoleId, RoleNames.User);

        Assert.False(decision.ApplicationReadOnly);
        Assert.Equal(RoleNames.User, decision.MembershipRoleName);
    }

    [Fact]
    public void Decide_CustomRole_IsReadOnlyAndKeepsTheRoleName()
    {
        var decision = ContributorInviteAccess.Decide(Guid.NewGuid(), "Caseworker");

        Assert.True(decision.ApplicationReadOnly);
        Assert.Equal("Caseworker", decision.MembershipRoleName);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("SuperAdmin")]
    [InlineData("Administrator")]
    public void Decide_RejectsPrivilegedRoles(string roleName)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ContributorInviteAccess.Decide(Guid.NewGuid(), roleName));

        Assert.Equal(ContributorInviteAccess.RoleNotAllowed, error.Message);
    }

    [Fact]
    public void Decide_RejectsAMissingRole()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ContributorInviteAccess.Decide(null, null));

        Assert.Equal(ContributorInviteAccess.RoleNotFound, error.Message);
    }
}
