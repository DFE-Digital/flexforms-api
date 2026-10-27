using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Applications.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using ApplicationEntity = GovUK.Dfe.FlexForms.Domain.Entities.Application;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryObjects.Applications;

public class GetContributorsForApplicationQueryObjectTests
{
    private readonly IUserFactory _userFactory;

    public GetContributorsForApplicationQueryObjectTests()
    {
        _userFactory = new UserFactory();
    }

    [Fact]
    public void Apply_WithValidApplicationId_ShouldReturnUsersWithPermissions()
    {
        // Arrange
        var applicationId = new ApplicationId(Guid.NewGuid());
        var creatorId = new UserId(Guid.NewGuid());
        var templateVersionId = new TemplateVersionId(Guid.NewGuid());
        var queryObject = new GetContributorsForApplicationQueryObject(applicationId, creatorId);

        var application = new ApplicationEntity(applicationId, "APP-001", templateVersionId, DateTime.UtcNow, creatorId);

        var users = new List<User>
        {
            new User(new UserId(Guid.NewGuid()), new RoleId(Guid.NewGuid()), "User 1", "user1@example.com", DateTime.UtcNow, null, null, null),
            new User(new UserId(Guid.NewGuid()), new RoleId(Guid.NewGuid()), "User 2", "user2@example.com", DateTime.UtcNow, null, null, null),
            new User(new UserId(Guid.NewGuid()), new RoleId(Guid.NewGuid()), "User 3", "user3@example.com", DateTime.UtcNow, null, null, null)
        };

        // Add permissions to users using factory
        _userFactory.AddPermissionToUser(users[0], applicationId.Value.ToString(), ResourceType.Application, new[] { AccessType.Read }, new UserId(Guid.NewGuid()), applicationId, DateTime.UtcNow);
        _userFactory.AddPermissionToUser(users[1], applicationId.Value.ToString(), ResourceType.Application, new[] { AccessType.Write }, new UserId(Guid.NewGuid()), applicationId, DateTime.UtcNow);
        // User 3 has no permissions for this application

        // Set up Application property on permissions
        foreach (var user in users)
        {
            foreach (var permission in user.Permissions.Where(p => p.ApplicationId == applicationId))
            {
                permission.GetType().GetProperty("Application")?.SetValue(permission, application);
            }
        }

        var query = users.AsQueryable();

        // Act
        var result = queryObject.Apply(query).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, u => u.Name == "User 1");
        Assert.Contains(result, u => u.Name == "User 2");
        Assert.DoesNotContain(result, u => u.Name == "User 3");
    }

    [Fact]
    public void Apply_WithNoUsers_ShouldReturnEmptyList()
    {
        // Arrange
        var applicationId = new ApplicationId(Guid.NewGuid());
        var queryObject = new GetContributorsForApplicationQueryObject(applicationId, new UserId(Guid.NewGuid()));
        var users = new List<User>().AsQueryable();

        // Act
        var result = queryObject.Apply(users).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Apply_WithNoUsersHavingPermissions_ShouldReturnEmptyList()
    {
        // Arrange
        var applicationId = new ApplicationId(Guid.NewGuid());
        var queryObject = new GetContributorsForApplicationQueryObject(applicationId, new UserId(Guid.NewGuid()));

        var users = new List<User>
        {
            new User(new UserId(Guid.NewGuid()), new RoleId(Guid.NewGuid()), "User 1", "user1@example.com", DateTime.UtcNow, null, null, null),
            new User(new UserId(Guid.NewGuid()), new RoleId(Guid.NewGuid()), "User 2", "user2@example.com", DateTime.UtcNow, null, null, null)
        };

        // No users have permissions for this application
        var query = users.AsQueryable();

        // Act
        var result = queryObject.Apply(query).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Apply_WithUsersHavingDifferentResourceTypes_ShouldOnlyReturnApplicationPermissions()
    {
        // Arrange
        var applicationId = new ApplicationId(Guid.NewGuid());
        var creatorId = new UserId(Guid.NewGuid());
        var templateVersionId = new TemplateVersionId(Guid.NewGuid());
        var queryObject = new GetContributorsForApplicationQueryObject(applicationId, creatorId);

        var application = new ApplicationEntity(applicationId, "APP-002", templateVersionId, DateTime.UtcNow, creatorId);

        var users = new List<User>
        {
            new User(new UserId(Guid.NewGuid()), new RoleId(Guid.NewGuid()), "User 1", "user1@example.com", DateTime.UtcNow, null, null, null),
            new User(new UserId(Guid.NewGuid()), new RoleId(Guid.NewGuid()), "User 2", "user2@example.com", DateTime.UtcNow, null, null, null)
        };

        // User 1 has Application permission
        _userFactory.AddPermissionToUser(users[0], applicationId.Value.ToString(), ResourceType.Application, new[] { AccessType.Read }, new UserId(Guid.NewGuid()), applicationId, DateTime.UtcNow);
        
        // User 2 has Template permission (should not be included)
        _userFactory.AddPermissionToUser(users[1], "template-id", ResourceType.Template, new[] { AccessType.Read }, new UserId(Guid.NewGuid()), null, DateTime.UtcNow);

        // Set up Application property on permissions
        foreach (var permission in users[0].Permissions.Where(p => p.ApplicationId == applicationId))
        {
            permission.GetType().GetProperty("Application")?.SetValue(permission, application);
        }

        var query = users.AsQueryable();

        // Act
        var result = queryObject.Apply(query).ToList();

        // Assert
        Assert.Single(result);
        Assert.Contains(result, u => u.Name == "User 1");
        Assert.DoesNotContain(result, u => u.Name == "User 2");
    }

    [Fact]
    public void Apply_WithUsersHavingDifferentApplicationIds_ShouldOnlyReturnMatchingApplicationPermissions()
    {
        // Arrange
        var applicationId1 = new ApplicationId(Guid.NewGuid());
        var applicationId2 = new ApplicationId(Guid.NewGuid());
        var creatorId1 = new UserId(Guid.NewGuid());
        var creatorId2 = new UserId(Guid.NewGuid());
        var templateVersionId1 = new TemplateVersionId(Guid.NewGuid());
        var templateVersionId2 = new TemplateVersionId(Guid.NewGuid());
        var queryObject = new GetContributorsForApplicationQueryObject(applicationId1, creatorId1);

        var application1 = new ApplicationEntity(applicationId1, "APP-003", templateVersionId1, DateTime.UtcNow, creatorId1);
        var application2 = new ApplicationEntity(applicationId2, "APP-004", templateVersionId2, DateTime.UtcNow, creatorId2);

        var users = new List<User>
        {
            new User(new UserId(Guid.NewGuid()), new RoleId(Guid.NewGuid()), "User 1", "user1@example.com", DateTime.UtcNow, null, null, null),
            new User(new UserId(Guid.NewGuid()), new RoleId(Guid.NewGuid()), "User 2", "user2@example.com", DateTime.UtcNow, null, null, null)
        };

        // User 1 has permission for applicationId1
        _userFactory.AddPermissionToUser(users[0], applicationId1.Value.ToString(), ResourceType.Application, new[] { AccessType.Read }, new UserId(Guid.NewGuid()), applicationId1, DateTime.UtcNow);
        
        // User 2 has permission for applicationId2 (should not be included)
        _userFactory.AddPermissionToUser(users[1], applicationId2.Value.ToString(), ResourceType.Application, new[] { AccessType.Read }, new UserId(Guid.NewGuid()), applicationId2, DateTime.UtcNow);

        // Set up Application property on permissions
        foreach (var permission in users[0].Permissions.Where(p => p.ApplicationId == applicationId1))
        {
            permission.GetType().GetProperty("Application")?.SetValue(permission, application1);
        }
        foreach (var permission in users[1].Permissions.Where(p => p.ApplicationId == applicationId2))
        {
            permission.GetType().GetProperty("Application")?.SetValue(permission, application2);
        }

        var query = users.AsQueryable();

        // Act
        var result = queryObject.Apply(query).ToList();

        // Assert
        Assert.Single(result);
        Assert.Contains(result, u => u.Name == "User 1");
        Assert.DoesNotContain(result, u => u.Name == "User 2");
    }

    [Fact]
    public void Apply_WithUsersHavingMultiplePermissions_ShouldReturnUsersOnce()
    {
        // Arrange
        var applicationId = new ApplicationId(Guid.NewGuid());
        var creatorId = new UserId(Guid.NewGuid());
        var templateVersionId = new TemplateVersionId(Guid.NewGuid());
        var queryObject = new GetContributorsForApplicationQueryObject(applicationId, creatorId);

        var application = new ApplicationEntity(applicationId, "APP-005", templateVersionId, DateTime.UtcNow, creatorId);

        var users = new List<User>
        {
            new User(new UserId(Guid.NewGuid()), new RoleId(Guid.NewGuid()), "User 1", "user1@example.com", DateTime.UtcNow, null, null, null)
        };

        // User has both Read and Write permissions for the same application
        _userFactory.AddPermissionToUser(users[0], applicationId.Value.ToString(), ResourceType.Application, new[] { AccessType.Read }, new UserId(Guid.NewGuid()), applicationId, DateTime.UtcNow);
        _userFactory.AddPermissionToUser(users[0], applicationId.Value.ToString(), ResourceType.Application, new[] { AccessType.Write }, new UserId(Guid.NewGuid()), applicationId, DateTime.UtcNow);

        // Set up Application property on permissions
        foreach (var permission in users[0].Permissions.Where(p => p.ApplicationId == applicationId))
        {
            permission.GetType().GetProperty("Application")?.SetValue(permission, application);
        }

        var query = users.AsQueryable();

        // Act
        var result = queryObject.Apply(query).ToList();

        // Assert
        Assert.Single(result);
        Assert.Contains(result, u => u.Name == "User 1");
    }

    [Fact]
    public void Apply_ShouldExcludeApplicationCreator_FromContributorsList()
    {
        // Arrange
        var applicationId = new ApplicationId(Guid.NewGuid());
        var creatorId = new UserId(Guid.NewGuid());
        var templateVersionId = new TemplateVersionId(Guid.NewGuid());
        var queryObject = new GetContributorsForApplicationQueryObject(applicationId, creatorId);

        var application = new ApplicationEntity(applicationId, "APP-006", templateVersionId, DateTime.UtcNow, creatorId);

        var users = new List<User>
        {
            new User(creatorId, new RoleId(Guid.NewGuid()), "Creator", "creator@example.com", DateTime.UtcNow, null, null, null), // Application creator
            new User(new UserId(Guid.NewGuid()), new RoleId(Guid.NewGuid()), "Contributor 1", "contributor1@example.com", DateTime.UtcNow, null, null, null),
            new User(new UserId(Guid.NewGuid()), new RoleId(Guid.NewGuid()), "Contributor 2", "contributor2@example.com", DateTime.UtcNow, null, null, null)
        };

        // All users have permissions for the application
        _userFactory.AddPermissionToUser(users[0], applicationId.Value.ToString(), ResourceType.Application, new[] { AccessType.Read }, new UserId(Guid.NewGuid()), applicationId, DateTime.UtcNow);
        _userFactory.AddPermissionToUser(users[1], applicationId.Value.ToString(), ResourceType.Application, new[] { AccessType.Read }, new UserId(Guid.NewGuid()), applicationId, DateTime.UtcNow);
        _userFactory.AddPermissionToUser(users[2], applicationId.Value.ToString(), ResourceType.Application, new[] { AccessType.Write }, new UserId(Guid.NewGuid()), applicationId, DateTime.UtcNow);

        // Set up Application property on permissions
        foreach (var user in users)
        {
            foreach (var permission in user.Permissions.Where(p => p.ApplicationId == applicationId))
            {
                permission.GetType().GetProperty("Application")?.SetValue(permission, application);
            }
        }

        var query = users.AsQueryable();

        // Act
        var result = queryObject.Apply(query).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, u => u.Name == "Contributor 1");
        Assert.Contains(result, u => u.Name == "Contributor 2");
        Assert.DoesNotContain(result, u => u.Name == "Creator"); // Creator should be excluded
    }
} 