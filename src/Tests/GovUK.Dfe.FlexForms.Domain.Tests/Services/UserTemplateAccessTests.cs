using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Domain.Tests.Services;

public class UserTemplateAccessTests
{
    [Fact]
    public void IsApplicationInviteOnly_ShouldBeTrue_ForNewContributorOnThisTenant()
    {
        var tenantTemplateId = Guid.NewGuid();
        var otherTenantTemplateId = Guid.NewGuid();
        var user = CreateUser(
            TemplateGrant(tenantTemplateId, AccessType.Read),
            ApplicationGrant(Guid.NewGuid()));

        Assert.True(UserTemplateAccess.IsApplicationInviteOnly(user, new HashSet<Guid> { tenantTemplateId }));
        Assert.False(UserTemplateAccess.HasWriteOnTenant(user, new HashSet<Guid> { tenantTemplateId }));
        Assert.False(UserTemplateAccess.HasWriteOnTenant(user, new HashSet<Guid> { otherTenantTemplateId }));
    }

    [Fact]
    public void IsApplicationInviteOnly_ShouldBeFalse_WhenUserAlreadyHasWriteOnThisTenant()
    {
        var templateA = Guid.NewGuid();
        var templateB = Guid.NewGuid();
        var user = CreateUser(
            TemplateGrant(templateA, AccessType.Read),
            TemplateGrant(templateA, AccessType.Write),
            TemplateGrant(templateB, AccessType.Read),
            TemplateGrant(templateB, AccessType.Write),
            ApplicationGrant(Guid.NewGuid()));

        Assert.False(UserTemplateAccess.IsApplicationInviteOnly(user, new HashSet<Guid> { templateA, templateB }));
        Assert.True(UserTemplateAccess.HasWrite(user, new TemplateId(templateA)));
        Assert.True(UserTemplateAccess.HasWrite(user, new TemplateId(templateB)));
    }

    [Fact]
    public void IsApplicationInviteOnly_ShouldIgnoreTemplateWriteOnOtherTenants()
    {
        var thisTenantTemplate = Guid.NewGuid();
        var otherTenantTemplate = Guid.NewGuid();
        var user = CreateUser(
            TemplateGrant(thisTenantTemplate, AccessType.Read),
            TemplateGrant(otherTenantTemplate, AccessType.Read),
            TemplateGrant(otherTenantTemplate, AccessType.Write),
            ApplicationGrant(Guid.NewGuid()));

        Assert.True(UserTemplateAccess.IsApplicationInviteOnly(
            user,
            new HashSet<Guid> { thisTenantTemplate }));
        Assert.False(UserTemplateAccess.IsApplicationInviteOnly(
            user,
            new HashSet<Guid> { otherTenantTemplate }));
    }

    [Fact]
    public void IsApplicationInviteOnly_ShouldBeFalse_WhenUserHasNoTenantTemplateReadYet()
    {
        var tenantTemplateId = Guid.NewGuid();
        var otherTenantTemplateId = Guid.NewGuid();
        var user = CreateUser(
            TemplateGrant(otherTenantTemplateId, AccessType.Write),
            ApplicationGrant(Guid.NewGuid()));

        Assert.False(UserTemplateAccess.IsApplicationInviteOnly(user, new HashSet<Guid> { tenantTemplateId }));
    }

    [Fact]
    public void HasWrite_ShouldTreatAnyKeyAsWrite()
    {
        var templateId = new TemplateId(Guid.NewGuid());
        var user = CreateUser(
            new Permission(
                new PermissionId(Guid.NewGuid()),
                new UserId(Guid.NewGuid()),
                applicationId: null,
                PermissionConstants.AnyResourceKey,
                ResourceType.Template,
                AccessType.Write,
                DateTime.UtcNow,
                new UserId(Guid.NewGuid())));

        Assert.True(UserTemplateAccess.HasWrite(user, templateId));
        Assert.True(UserTemplateAccess.HasWriteOnTenant(user, new HashSet<Guid> { templateId.Value }));
        Assert.False(UserTemplateAccess.IsApplicationInviteOnly(user, new HashSet<Guid> { templateId.Value }));
    }

    private static User CreateUser(params Permission[] permissions) =>
        new User(
            new UserId(Guid.NewGuid()),
            new RoleId(Guid.NewGuid()),
            "Existing User",
            "existing@example.com",
            DateTime.UtcNow,
            null,
            null,
            null,
            initialPermissions: permissions);

    private static Permission TemplateGrant(Guid templateId, AccessType accessType) =>
        new(
            new PermissionId(Guid.NewGuid()),
            new UserId(Guid.NewGuid()),
            applicationId: null,
            templateId.ToString(),
            ResourceType.Template,
            accessType,
            DateTime.UtcNow,
            new UserId(Guid.NewGuid()));

    private static Permission ApplicationGrant(Guid applicationId) =>
        new(
            new PermissionId(Guid.NewGuid()),
            new UserId(Guid.NewGuid()),
            new ApplicationId(applicationId),
            applicationId.ToString(),
            ResourceType.Application,
            AccessType.Write,
            DateTime.UtcNow,
            new UserId(Guid.NewGuid()));
}
