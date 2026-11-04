using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Services;

namespace GovUK.Dfe.FlexForms.Domain.Tests.Services;

public class RoleTemplatesTests
{
    [Theory]
    [InlineData(AccessType.Write)]
    [InlineData(AccessType.Manage)]
    public void TemplateManager_GrantsTenantWideTemplateAccess(AccessType accessType)
    {
        var template = RoleTemplates.Get(RoleTemplates.TemplateManagerKey);

        Assert.NotNull(template);
        Assert.Contains(
            new RoleTemplates.Grant(ResourceType.Template, PermissionConstants.AnyResourceKey, accessType),
            template!.Grants);
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
