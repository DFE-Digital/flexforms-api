using AutoFixture;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryObjects.Users;

public class GetUserWithAllTemplatePermissionsByExternalIdQueryObjectTests
{
    [Theory]
    [CustomAutoData(typeof(UserCustomization), typeof(PermissionCustomization))]
    public void Apply_ShouldReturnMatchingUser_WhenExternalProviderIdMatches(
        string externalProviderId)
    {
        // Arrange
        var matchingUser = new Fixture()
            .Customize(new UserCustomization { OverrideExternalProviderId = externalProviderId })
            .Create<User>();

        var backingField = typeof(User)
            .GetField("_permissions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        backingField.SetValue(matchingUser, new List<Permission>());

        var templatePermission = new Permission(
            new PermissionId(Guid.NewGuid()),
            matchingUser.Id!,
            applicationId: null,
            Guid.NewGuid().ToString(),
            ResourceType.Template,
            AccessType.Read,
            DateTime.UtcNow,
            matchingUser.Id!);
        ((List<Permission>)backingField.GetValue(matchingUser)!).Add(templatePermission);

        var otherUser1 = new Fixture()
            .Customize(new UserCustomization { OverrideExternalProviderId = "other-id-1" })
            .Create<User>();

        var otherUser2 = new Fixture()
            .Customize(new UserCustomization { OverrideExternalProviderId = "other-id-2" })
            .Create<User>();

        var users = new[] { matchingUser, otherUser1, otherUser2 }.AsQueryable();
        var queryObject = new GetUserWithAllTemplatePermissionsByExternalIdQueryObject(externalProviderId);

        // Act
        var result = queryObject.Apply(users).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal(externalProviderId, result[0].ExternalProviderId);
        Assert.Single(result[0].Permissions.Where(p => p.ResourceType == ResourceType.Template));
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public void Apply_ShouldReturnEmpty_WhenNoUserMatches(
        string externalProviderId)
    {
        // Arrange
        var user1 = new Fixture()
            .Customize(new UserCustomization { OverrideExternalProviderId = "other-id-1" })
            .Create<User>();

        var user2 = new Fixture()
            .Customize(new UserCustomization { OverrideExternalProviderId = "other-id-2" })
            .Create<User>();

        var users = new[] { user1, user2 }.AsQueryable();
        var queryObject = new GetUserWithAllTemplatePermissionsByExternalIdQueryObject(externalProviderId);

        // Act
        var result = queryObject.Apply(users).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(" ")]
    public void Apply_ShouldReturnEmpty_WhenExternalProviderIdIsNullOrEmpty(
        string externalProviderId)
    {
        // Arrange — seed users with non-null provider ids so a null/empty filter does not match them
        var user1 = new Fixture()
            .Customize(new UserCustomization { OverrideExternalProviderId = "provider-1" })
            .Create<User>();
        var user2 = new Fixture()
            .Customize(new UserCustomization { OverrideExternalProviderId = "provider-2" })
            .Create<User>();

        var users = new[] { user1, user2 }.AsQueryable();
        var queryObject = new GetUserWithAllTemplatePermissionsByExternalIdQueryObject(externalProviderId);

        // Act
        var result = queryObject.Apply(users).ToList();

        // Assert
        Assert.Empty(result);
    }
}
