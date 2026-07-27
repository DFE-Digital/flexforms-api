using GovUK.Dfe.FlexForms.Domain.Common;

namespace GovUK.Dfe.FlexForms.Domain.Tests.Common;

public class RoleNamesTests
{
    [Theory]
    [InlineData(RoleNames.User)]
    [InlineData("user")]
    public void IsAssignable_ShouldAllowUser(string roleName)
    {
        Assert.True(RoleNames.IsAssignable(roleName));
        Assert.Equal(RoleNames.User, RoleNames.ResolveAssignable(roleName));
    }

    [Theory]
    [InlineData(RoleNames.SuperAdmin)]
    [InlineData(RoleNames.Admin)]
    [InlineData(RoleNames.Caseworker)]
    [InlineData("Administrator")]
    [InlineData("superadmin")]
    public void IsAssignable_ShouldRejectNonUserRoles(string roleName)
    {
        Assert.False(RoleNames.IsAssignable(roleName));
        Assert.Null(RoleNames.ResolveAssignable(roleName));
    }

    [Theory]
    [InlineData(RoleNames.SuperAdmin)]
    [InlineData(RoleNames.Admin)]
    [InlineData("Administrator")]
    [InlineData(" admin ")]
    public void IsReservedRoleName_ShouldMatchPlatformNames(string roleName)
    {
        Assert.True(RoleNames.IsReservedRoleName(roleName));
    }

    [Theory]
    [InlineData(RoleNames.User)]
    [InlineData(RoleNames.Caseworker)]
    [InlineData("CustomReviewer")]
    public void IsReservedRoleName_ShouldAllowTenantAndCustomNames(string roleName)
    {
        Assert.False(RoleNames.IsReservedRoleName(roleName));
    }

    [Fact]
    public void Assignable_ShouldOnlyIncludeUser()
    {
        Assert.Equal(new[] { RoleNames.User }, RoleNames.Assignable);
        Assert.DoesNotContain(RoleNames.SuperAdmin, RoleNames.Assignable);
        Assert.DoesNotContain(RoleNames.Caseworker, RoleNames.Assignable);
    }

    [Fact]
    public void IsDowngradeToUser_ShouldProtectOnlyPlatformSuperAdmin()
    {
        Assert.True(RoleNames.IsDowngradeToUser(RoleNames.SuperAdmin, RoleNames.User));
        Assert.True(RoleNames.IsDowngradeToUser(RoleNames.Admin, RoleNames.User));
        Assert.False(RoleNames.IsDowngradeToUser(RoleNames.Caseworker, RoleNames.User));
        Assert.False(RoleNames.IsDowngradeToUser(RoleNames.User, RoleNames.User));
    }
}
