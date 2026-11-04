using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Services;

namespace GovUK.Dfe.FlexForms.Domain.Tests.Services;

public class RoleTemplatesTests
{
    [Fact]
    public void TemplateManager_GrantsTenantWideManageOnly()
    {
        var template = RoleTemplates.Get(RoleTemplates.TemplateManagerKey);

        Assert.NotNull(template);
        var grant = Assert.Single(template!.Grants);
        Assert.Equal(
            new RoleTemplates.Grant(ResourceType.Template, PermissionConstants.AnyResourceKey, AccessType.Manage),
            grant);
    }

    [Fact]
    public void AllPresetGrants_PassRoleGrantValidation()
    {
        foreach (var grant in RoleTemplates.All.SelectMany(t => t.Grants))
        {
            RolePermissionGrantRules.EnsureValid(grant.ResourceType, grant.ResourceKey, grant.AccessType);
        }
    }
}
