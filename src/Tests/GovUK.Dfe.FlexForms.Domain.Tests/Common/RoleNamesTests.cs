using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Services;

namespace GovUK.Dfe.FlexForms.Domain.Tests.Common;

public class RoleNamesTests
{
    [Theory]
    [InlineData(RoleNames.Admin)]
    [InlineData("admin")]
    [InlineData(RoleNames.User)]
    [InlineData("user")]
    public void IsAssignable_ShouldAllowTenantSystemRoles(string roleName)
    {
        Assert.True(RoleNames.IsAssignable(roleName));
        Assert.NotNull(RoleNames.ResolveAssignable(roleName));
    }

    [Theory]
    [InlineData(RoleNames.SuperAdmin)]
    [InlineData(RoleTemplates.CaseworkerKey)]
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
    [InlineData(RoleTemplates.CaseworkerKey)]
    [InlineData("CustomReviewer")]
    public void IsReservedRoleName_ShouldAllowTenantAndCustomNames(string roleName)
    {
        Assert.False(RoleNames.IsReservedRoleName(roleName));
    }

    [Fact]
    public void Assignable_ShouldIncludeAdminAndUser()
    {
        Assert.Equal(new[] { RoleNames.Admin, RoleNames.User }, RoleNames.Assignable);
        Assert.DoesNotContain(RoleNames.SuperAdmin, RoleNames.Assignable);
        Assert.DoesNotContain(RoleTemplates.CaseworkerKey, RoleNames.Assignable);
    }

    [Fact]
    public void IsPlatformSuperAdminUser_ShouldMatchWellKnownAdminRoleId()
    {
        Assert.True(RoleNames.IsPlatformSuperAdminRoleId(RoleConstants.AdminRoleId));
        Assert.True(RoleNames.IsPlatformSuperAdminUser(RoleNames.Admin, RoleConstants.AdminRoleId));
        Assert.True(RoleNames.IsPlatformSuperAdminUser(RoleNames.SuperAdmin, RoleConstants.AdminRoleId));
        Assert.True(RoleNames.IsPlatformSuperAdminUser(null, RoleConstants.AdminRoleId));
        Assert.False(RoleNames.IsPlatformSuperAdminUser(RoleNames.User, RoleConstants.UserRoleId));
        Assert.False(RoleNames.IsPlatformSuperAdminUser(RoleNames.Admin, Guid.NewGuid()));
    }

    [Fact]
    public void FromRoleId_AdminRoleId_ShouldMapToSuperAdmin()
    {
        Assert.Equal(RoleNames.SuperAdmin, RoleNames.FromRoleId(RoleConstants.AdminRoleId));
    }

    [Fact]
    public void IsDowngradeToUser_ShouldProtectOnlyPlatformSuperAdmin()
    {
        Assert.True(RoleNames.IsDowngradeToUser(RoleNames.SuperAdmin, RoleNames.User));
        Assert.False(RoleNames.IsDowngradeToUser(RoleNames.Admin, RoleNames.User));
        Assert.False(RoleNames.IsDowngradeToUser(RoleTemplates.CaseworkerKey, RoleNames.User));
        Assert.False(RoleNames.IsDowngradeToUser(RoleNames.User, RoleNames.User));
    }
}
