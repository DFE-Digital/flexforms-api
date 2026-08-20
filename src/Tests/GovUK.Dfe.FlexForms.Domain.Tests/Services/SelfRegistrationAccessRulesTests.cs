using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Domain.Tests.Services;

public class SelfRegistrationAccessRulesTests
{
    [Fact]
    public void ResolveAutoGrantedTemplates_ReturnsEmpty_WhenNoLiveTemplates()
    {
        Assert.Empty(SelfRegistrationAccessRules.ResolveAutoGrantedTemplates(Array.Empty<TemplateId>()));
    }

    [Fact]
    public void ResolveAutoGrantedTemplates_ReturnsTheOnlyLiveTemplate()
    {
        var only = new TemplateId(Guid.NewGuid());

        var result = SelfRegistrationAccessRules.ResolveAutoGrantedTemplates([only]);

        var granted = Assert.Single(result);
        Assert.Equal(only, granted);
    }

    [Fact]
    public void ResolveAutoGrantedTemplates_ReturnsEmpty_WhenSeveralLiveAndNoDefault()
    {
        var first = new TemplateId(Guid.NewGuid());
        var second = new TemplateId(Guid.NewGuid());

        Assert.Empty(SelfRegistrationAccessRules.ResolveAutoGrantedTemplates([first, second]));
    }

    [Fact]
    public void ResolveAutoGrantedTemplates_ReturnsDefault_WhenSeveralLiveAndDefaultIsLive()
    {
        var first = new TemplateId(Guid.NewGuid());
        var second = new TemplateId(Guid.NewGuid());

        var result = SelfRegistrationAccessRules.ResolveAutoGrantedTemplates([first, second], second);

        var granted = Assert.Single(result);
        Assert.Equal(second, granted);
    }

    [Fact]
    public void ResolveAutoGrantedTemplates_ReturnsEmpty_WhenDefaultIsNotLive()
    {
        var first = new TemplateId(Guid.NewGuid());
        var second = new TemplateId(Guid.NewGuid());
        var other = new TemplateId(Guid.NewGuid());

        Assert.Empty(SelfRegistrationAccessRules.ResolveAutoGrantedTemplates([first, second], other));
    }

    [Fact]
    public void ResolveAutoGrantedTemplates_IgnoresDefault_WhenOnlyOneFormIsLive()
    {
        var only = new TemplateId(Guid.NewGuid());
        var unusedDefault = new TemplateId(Guid.NewGuid());

        var result = SelfRegistrationAccessRules.ResolveAutoGrantedTemplates([only], unusedDefault);

        var granted = Assert.Single(result);
        Assert.Equal(only, granted);
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
            initialPermissions:
            [
                new Permission(
                    new PermissionId(Guid.NewGuid()),
                    userId,
                    applicationId: null,
                    templateId.Value.ToString(),
                    ResourceType.Template,
                    AccessType.Write,
                    DateTime.UtcNow,
                    userId)
            ]);

        Assert.True(SelfRegistrationAccessRules.HasTemplateAccess(user, templateId));
        Assert.False(SelfRegistrationAccessRules.HasTemplateAccess(user, new TemplateId(Guid.NewGuid())));
    }
}
