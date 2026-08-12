using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Services.RoleProvisioners;
using GovUK.Dfe.FlexForms.Domain.Factories;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Domain.Tests.Services;

public class UserRoleProvisionerRegistryTests
{
    [Fact]
    public void GetProvisioner_ShouldResolveUserProvisioner()
    {
        var userFactory = new UserFactory();
        var registry = new UserRoleProvisionerRegistry([
            new StandardUserRoleProvisioner(userFactory),
            new AdminRoleProvisioner(userFactory)
        ]);

        var provisioner = registry.GetProvisioner("user");

        Assert.NotNull(provisioner);
        Assert.Equal(RoleNames.User, provisioner!.RoleName);
    }

    [Fact]
    public void GetProvisioner_ShouldResolveAdminProvisioner()
    {
        var userFactory = new UserFactory();
        var registry = new UserRoleProvisionerRegistry([
            new StandardUserRoleProvisioner(userFactory),
            new AdminRoleProvisioner(userFactory)
        ]);

        var provisioner = registry.GetProvisioner("admin");

        Assert.NotNull(provisioner);
        Assert.Equal(RoleNames.Admin, provisioner!.RoleName);
    }

    [Fact]
    public void GetProvisioner_ShouldReturnNull_ForUnknownRole()
    {
        var userFactory = Substitute.For<IUserFactory>();
        var registry = new UserRoleProvisionerRegistry([
            new AdminRoleProvisioner(userFactory)
        ]);

        Assert.Null(registry.GetProvisioner("Unknown"));
    }

    [Fact]
    public void GetProvisioner_ShouldReturnNull_ForCaseworkerAndSuperAdmin()
    {
        var userFactory = new UserFactory();
        var registry = new UserRoleProvisionerRegistry([
            new StandardUserRoleProvisioner(userFactory),
            new AdminRoleProvisioner(userFactory)
        ]);

        Assert.Null(registry.GetProvisioner(RoleTemplates.CaseworkerKey));
        Assert.Null(registry.GetProvisioner(RoleNames.SuperAdmin));
    }
}
