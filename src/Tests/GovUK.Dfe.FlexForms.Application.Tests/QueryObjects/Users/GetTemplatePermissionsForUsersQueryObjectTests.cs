using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using FluentAssertions;
using MockQueryable;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryObjects.Users;

public class GetTemplatePermissionsForUsersQueryObjectTests
{
    [Fact]
    public void Apply_ShouldReturnNoRows_WhenUserIdsAreEmpty()
    {
        var permission = CreatePermission(new UserId(Guid.NewGuid()), ResourceType.Template);

        var result = new GetTemplatePermissionsForUsersQueryObject([])
            .Apply(new[] { permission }.AsQueryable().BuildMock())
            .ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public void Apply_ShouldReturnTemplatePermissionsForRequestedUsersOnly()
    {
        var included = new UserId(Guid.NewGuid());
        var excluded = new UserId(Guid.NewGuid());
        var keep = CreatePermission(included, ResourceType.Template);
        var otherUser = CreatePermission(excluded, ResourceType.Template);
        var applicationGrant = CreatePermission(included, ResourceType.Application);

        var result = new GetTemplatePermissionsForUsersQueryObject([included])
            .Apply(new[] { keep, otherUser, applicationGrant }.AsQueryable().BuildMock())
            .ToList();

        var permission = Assert.Single(result);
        Assert.Equal(keep.Id, permission.Id);
    }

    private static Permission CreatePermission(UserId userId, ResourceType resourceType)
    {
        return new Permission(
            new PermissionId(Guid.NewGuid()),
            userId,
            applicationId: null,
            Guid.NewGuid().ToString(),
            resourceType,
            AccessType.Read,
            DateTime.UtcNow,
            userId);
    }
}
