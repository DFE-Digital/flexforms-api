using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Services;

namespace GovUK.Dfe.FlexForms.Domain.Tests.Services;

public class RolePermissionGrantRulesTests
{
    [Fact]
    public void EnsureValid_AllowsTemplateAnyWrite()
    {
        RolePermissionGrantRules.EnsureValid(
            ResourceType.Template,
            PermissionConstants.AnyResourceKey,
            AccessType.Write);
    }

    [Fact]
    public void EnsureValid_AllowsApplicationAnyRead()
    {
        RolePermissionGrantRules.EnsureValid(
            ResourceType.Application,
            PermissionConstants.AnyResourceKey,
            AccessType.Read);
    }

    [Fact]
    public void EnsureValid_AllowsApplicationFilesAnyRead()
    {
        RolePermissionGrantRules.EnsureValid(
            ResourceType.ApplicationFiles,
            PermissionConstants.AnyResourceKey,
            AccessType.Read);
    }

    [Fact]
    public void EnsureValid_AllowsTemplateManageWrite()
    {
        RolePermissionGrantRules.EnsureValid(
            ResourceType.Template,
            PermissionConstants.ManageResourceKey,
            AccessType.Write);
    }

    [Theory]
    [InlineData(ResourceType.Application, AccessType.Write)]
    [InlineData(ResourceType.ApplicationFiles, AccessType.Write)]
    [InlineData(ResourceType.User, AccessType.Read)]
    [InlineData(ResourceType.Template, AccessType.Read)]
    public void EnsureValid_RejectsAny_ForDisallowedGrants(ResourceType resourceType, AccessType accessType)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            RolePermissionGrantRules.EnsureValid(
                resourceType,
                PermissionConstants.AnyResourceKey,
                accessType));

        Assert.Contains("only allowed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureValid_RejectsManage_ForNonTemplateWrite()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            RolePermissionGrantRules.EnsureValid(
                ResourceType.Application,
                PermissionConstants.ManageResourceKey,
                AccessType.Write));

        Assert.Contains("Manage", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureValid_RequiresGuid_ForApplication()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            RolePermissionGrantRules.EnsureValid(
                ResourceType.Application,
                "not-a-guid",
                AccessType.Read));

        Assert.Contains("GUID", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureValid_RequiresEmail_ForUser()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            RolePermissionGrantRules.EnsureValid(
                ResourceType.User,
                "not-an-email",
                AccessType.Read));

        Assert.Contains("email", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureValid_AcceptsApplicationGuid()
    {
        RolePermissionGrantRules.EnsureValid(
            ResourceType.Application,
            Guid.NewGuid().ToString(),
            AccessType.Read);
    }
}
