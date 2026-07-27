using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using FluentAssertions;
using MockQueryable;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryObjects.Users;

public class GetUserWithTemplatePermissionsByUserIdQueryObjectTests
{
    [Fact]
    public void Apply_ShouldReturnMatchingUser_WithTemplatePermissions()
    {
        var userId = new UserId(Guid.NewGuid());
        var otherId = new UserId(Guid.NewGuid());
        var templateId = new TemplateId(Guid.NewGuid());

        var matching = new User(
            userId,
            new RoleId(RoleConstants.UserRoleId),
            "Match",
            "match@example.com",
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
                    AccessType.Read,
                    DateTime.UtcNow,
                    userId)
            ]);

        var other = new User(
            otherId,
            new RoleId(RoleConstants.UserRoleId),
            "Other",
            "other@example.com",
            DateTime.UtcNow,
            null,
            null,
            null);

        var result = new GetUserWithTemplatePermissionsByUserIdQueryObject(userId)
            .Apply(new[] { matching, other }.AsQueryable().BuildMock())
            .ToList();

        result.Should().ContainSingle();
        result[0].Id.Should().Be(userId);
        result[0].TemplatePermissions.Should().ContainSingle(tp => tp.TemplateId == templateId);
    }

    [Fact]
    public void Apply_ShouldReturnEmpty_WhenUserIdDoesNotMatch()
    {
        var user = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(RoleConstants.UserRoleId),
            "Someone",
            "someone@example.com",
            DateTime.UtcNow,
            null,
            null,
            null);

        var result = new GetUserWithTemplatePermissionsByUserIdQueryObject(new UserId(Guid.NewGuid()))
            .Apply(new[] { user }.AsQueryable().BuildMock())
            .ToList();

        result.Should().BeEmpty();
    }
}
