using AutoFixture;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.FlexForms.Application.Templates.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryObjects.Templates;

public class GetTemplatePermissionByUserIdQueryObjectTests
{
    [Theory]
    [CustomAutoData(typeof(PermissionCustomization))]
    public void Apply_ShouldReturnMatchingPermission_WhenUserIdAndTemplateIdMatch(
        UserId userId,
        PermissionCustomization permCustom)
    {
        // Arrange
        var template = new Fixture().Customize(new TemplateCustomization()).Create<Template>();
        var user = new Fixture().Customize(new UserCustomization()).Create<User>();
        var otherUser = new Fixture().Customize(new UserCustomization()).Create<User>();

        permCustom.OverrideUserId = user.Id;
        permCustom.OverrideAppId = null;
        permCustom.OverrideResourceType = ResourceType.Template;
        permCustom.OverrideResourceKey = template.Id!.Value.ToString();
        permCustom.OverrideAccessType = AccessType.Read;
        var matchingPermission = new Fixture().Customize(permCustom).Create<Permission>();
        typeof(Permission).GetProperty(nameof(Permission.User))!.SetValue(matchingPermission, user);

        permCustom.OverrideUserId = otherUser.Id;
        var otherPermission = new Fixture().Customize(permCustom).Create<Permission>();
        typeof(Permission).GetProperty(nameof(Permission.User))!.SetValue(otherPermission, otherUser);

        var permissions = new[] { matchingPermission, otherPermission }.AsQueryable();
        var queryObject = new GetTemplatePermissionByUserIdQueryObject(user.Id!, template.Id.Value);

        // Act
        var result = queryObject.Apply(permissions).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal(user.Id, result[0].User!.Id);
        Assert.Equal(ResourceType.Template, result[0].ResourceType);
        Assert.Equal(template.Id.Value.ToString(), result[0].ResourceKey);
    }

    [Theory]
    [CustomAutoData(typeof(PermissionCustomization))]
    public void Apply_ShouldReturnEmpty_WhenNoPermissionsMatch(
        UserId userId,
        PermissionCustomization permCustom)
    {
        // Arrange
        var template = new Fixture().Customize(new TemplateCustomization()).Create<Template>();
        var user = new Fixture().Customize(new UserCustomization()).Create<User>();

        permCustom.OverrideUserId = user.Id;
        permCustom.OverrideAppId = null;
        permCustom.OverrideResourceType = ResourceType.Template;
        permCustom.OverrideResourceKey = template.Id!.Value.ToString();
        permCustom.OverrideAccessType = AccessType.Read;
        var permission = new Fixture().Customize(permCustom).Create<Permission>();
        typeof(Permission).GetProperty(nameof(Permission.User))!.SetValue(permission, user);

        var permissions = new[] { permission }.AsQueryable();
        var queryObject = new GetTemplatePermissionByUserIdQueryObject(userId, Guid.NewGuid());

        // Act
        var result = queryObject.Apply(permissions).ToList();

        // Assert
        Assert.Empty(result);
    }
}
