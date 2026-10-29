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

        var claims = PermissionClaimMerger.Merge(role, []);

        Assert.Contains("Application:Any:Read", claims);
    }

    [Fact]
    public void Merge_KeepsDifferentRoleAccessTypes_WhenUserAddsAGrantForTheSameKey()
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

        var claims = PermissionClaimMerger.Merge(role, user);

        Assert.Contains("Application:Any:Read", claims);
        Assert.Contains("Application:Any:Write", claims);
        Assert.Contains("Application:Any:Delete", claims);
    }

    [Fact]
    public void Merge_DeduplicatesTheSameRoleAndUserGrant()
    {
        var role = new[]
        {
            new PermissionClaimMerger.Grant(ResourceType.Template, "form-1", AccessType.Read)
        };
        var user = new[]
        {
            new PermissionClaimMerger.Grant(ResourceType.Template, "FORM-1", AccessType.Read)
        };

        var claims = PermissionClaimMerger.Merge(role, user);

        Assert.Single(claims);
        Assert.Contains("Template:FORM-1:Read", claims, StringComparer.OrdinalIgnoreCase);
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

        var claims = PermissionClaimMerger.Merge(role, user);

        Assert.Contains("Application:Any:Read", claims);
        Assert.Contains("Application:Any:Write", claims);
        Assert.Contains("Template:t1:Read", claims);
    }

    [Fact]
    public void Merge_AddsTemplateGrants_FromUserPermissions()
    {
        var templateId = Guid.NewGuid();
        var claims = PermissionClaimMerger.Merge(
            [],
            [new PermissionClaimMerger.Grant(ResourceType.Template, templateId.ToString(), AccessType.Read)]);

        Assert.Contains($"Template:{templateId}:Read", claims);
    }

    [Fact]
    public void Merge_SuppressesRoleApplicationWrite_WhenUserHasReadOnlyGrantOnSameApplication()
    {
        var applicationId = Guid.NewGuid().ToString();
        var role = new[]
        {
            new PermissionClaimMerger.Grant(ResourceType.Application, applicationId, AccessType.Write)
        };
        var user = new[]
        {
            new PermissionClaimMerger.Grant(ResourceType.Application, applicationId, AccessType.Read)
        };

        var claims = PermissionClaimMerger.Merge(role, user);

        Assert.DoesNotContain($"Application:{applicationId}:Write", claims, StringComparer.OrdinalIgnoreCase);
        Assert.Contains($"Application:{applicationId}:Read", claims, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Merge_SuppressesRoleApplicationFilesDelete_WhenUserHasReadOnlyFileGrant()
    {
        var applicationId = Guid.NewGuid().ToString();
        var role = new[]
        {
            new PermissionClaimMerger.Grant(ResourceType.ApplicationFiles, applicationId, AccessType.Delete)
        };
        var user = new[]
        {
            new PermissionClaimMerger.Grant(ResourceType.ApplicationFiles, applicationId, AccessType.Read)
        };

        var claims = PermissionClaimMerger.Merge(role, user);

        Assert.DoesNotContain($"ApplicationFiles:{applicationId}:Delete", claims, StringComparer.OrdinalIgnoreCase);
        Assert.Contains($"ApplicationFiles:{applicationId}:Read", claims, StringComparer.OrdinalIgnoreCase);
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

        var claims = PermissionClaimMerger.Merge(role, user);

        Assert.Equal(2, claims.Count);
        Assert.Contains("User:Alice@Example.com:Read", claims, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("User:alice@example.com:Write", claims, StringComparer.OrdinalIgnoreCase);
    }
}
