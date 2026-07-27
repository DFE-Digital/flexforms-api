using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Services;

namespace GovUK.Dfe.FlexForms.Domain.Tests.Services;

public class PermissionClaimMergerTests
{
    [Fact]
    public void Merge_IncludesRoleGrants_WhenUserHasNoOverrideForKey()
    {
        var role = new[]
        {
            new PermissionClaimMerger.Grant(ResourceType.Application, "Any", AccessType.Read)
        };

        var claims = PermissionClaimMerger.Merge(role, [], []);

        Assert.Contains("Application:Any:Read", claims);
    }

    [Fact]
    public void Merge_OmitsRoleGrants_WhenUserOverridesSameTypeAndKey()
    {
        var role = new[]
        {
            new PermissionClaimMerger.Grant(ResourceType.Application, "Any", AccessType.Read),
            new PermissionClaimMerger.Grant(ResourceType.Application, "Any", AccessType.Write)
        };
        var user = new[]
        {
            new PermissionClaimMerger.Grant(ResourceType.Application, "Any", AccessType.Delete)
        };

        var claims = PermissionClaimMerger.Merge(role, user, []);

        Assert.DoesNotContain("Application:Any:Read", claims);
        Assert.DoesNotContain("Application:Any:Write", claims);
        Assert.Contains("Application:Any:Delete", claims);
    }

    [Fact]
    public void Merge_KeepsRoleGrants_ForDifferentKeys()
    {
        var role = new[]
        {
            new PermissionClaimMerger.Grant(ResourceType.Application, "Any", AccessType.Read),
            new PermissionClaimMerger.Grant(ResourceType.Template, "t1", AccessType.Read)
        };
        var user = new[]
        {
            new PermissionClaimMerger.Grant(ResourceType.Application, "Any", AccessType.Write)
        };

        var claims = PermissionClaimMerger.Merge(role, user, []);

        Assert.DoesNotContain("Application:Any:Read", claims);
        Assert.Contains("Application:Any:Write", claims);
        Assert.Contains("Template:t1:Read", claims);
    }

    [Fact]
    public void Merge_AddsTemplateGrants()
    {
        var templateId = Guid.NewGuid();
        var claims = PermissionClaimMerger.Merge(
            [],
            [],
            [(templateId, AccessType.Read)]);

        Assert.Contains($"Template:{templateId}:Read", claims);
    }

    [Fact]
    public void Merge_IsCaseInsensitive_OnResourceKey()
    {
        var role = new[]
        {
            new PermissionClaimMerger.Grant(ResourceType.User, "Alice@Example.com", AccessType.Read)
        };
        var user = new[]
        {
            new PermissionClaimMerger.Grant(ResourceType.User, "alice@example.com", AccessType.Write)
        };

        var claims = PermissionClaimMerger.Merge(role, user, []);

        Assert.Single(claims);
        Assert.Contains("User:alice@example.com:Write", claims, StringComparer.OrdinalIgnoreCase);
    }
}
