using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Domain.Tests.Services;

public class SelfRegistrationAccessRulesTests
{
    [Fact]
    public void ResolveAutoGrantedTemplates_ReturnsAllLiveTemplates()
    {
        var first = new TemplateId(Guid.NewGuid());
        var second = new TemplateId(Guid.NewGuid());

        var result = SelfRegistrationAccessRules.ResolveAutoGrantedTemplates([first, second]);

        Assert.Equal(2, result.Count);
        Assert.Contains(first, result);
        Assert.Contains(second, result);
    }

    [Fact]
    public void ResolveAutoGrantedTemplates_ReturnsEmpty_WhenNoLiveTemplates()
    {
        Assert.Empty(SelfRegistrationAccessRules.ResolveAutoGrantedTemplates(Array.Empty<TemplateId>()));
    }

    [Fact]
    public void HasTemplateAccess_ReturnsExpectedResult()
    {
        var userId = new UserId(Guid.NewGuid());
        var templateId = new TemplateId(Guid.NewGuid());
        var user = new User(
            userId,
            new RoleId(RoleConstants.UserRoleId),
            "Test User",
            "user@example.com",
            DateTime.UtcNow,
            null,
            null,
            null,
            initialTemplatePermissions:
            [
                new TemplatePermission(
                    new TemplatePermissionId(Guid.NewGuid()),
                    userId,
                    templateId,
                    AccessType.Write,
                    DateTime.UtcNow,
                    userId)
            ]);

        Assert.True(SelfRegistrationAccessRules.HasTemplateAccess(user, templateId));
        Assert.False(SelfRegistrationAccessRules.HasTemplateAccess(user, new TemplateId(Guid.NewGuid())));
    }
}
