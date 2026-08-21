using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MockQueryable;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryObjects.Users;

public class GetApplicationIdsByUserIdQueryObjectTests
{
    [Fact]
    public void Apply_ReturnsDistinctApplicationIds_Only()
    {
        var userId = new UserId(Guid.NewGuid());
        var app1 = new ApplicationId(Guid.NewGuid());
        var app2 = new ApplicationId(Guid.NewGuid());

        var user = new User(
            userId,
            new RoleId(RoleConstants.UserRoleId),
            "User",
            "user@example.com",
            DateTime.UtcNow,
            null,
            null,
            null);

        var permissions = new List<Permission>
        {
            new(new PermissionId(Guid.NewGuid()), userId, app1, app1.Value.ToString(), ResourceType.Application, AccessType.Read, DateTime.UtcNow, userId),
            new(new PermissionId(Guid.NewGuid()), userId, app1, app1.Value.ToString(), ResourceType.Application, AccessType.Write, DateTime.UtcNow, userId),
            new(new PermissionId(Guid.NewGuid()), userId, app2, app2.Value.ToString(), ResourceType.Application, AccessType.Read, DateTime.UtcNow, userId),
            new(new PermissionId(Guid.NewGuid()), userId, null, Guid.NewGuid().ToString(), ResourceType.Template, AccessType.Read, DateTime.UtcNow, userId)
        };

        typeof(User).GetField("_permissions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(user, permissions);

        var ids = new GetApplicationIdsByUserIdQueryObject(userId)
            .Apply(new List<User> { user }.AsQueryable().BuildMock())
            .ToList();

        Assert.Equal(2, ids.Count);
        Assert.Contains(app1, ids);
        Assert.Contains(app2, ids);
    }
}
