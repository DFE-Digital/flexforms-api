using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Events;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Xunit;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class UserFactoryTests
{
    private readonly IUserFactory _userFactory;

    public UserFactoryTests()
    {
        _userFactory = new UserFactory();
    }

    [Fact]
    public void CreateContributor_WithValidParameters_ShouldCreateUserWithDomainEvent()
    {
        // Arrange
        var userId = new UserId(Guid.NewGuid());
        var roleId = new RoleId(Guid.NewGuid());
        var createdBy = new UserId(Guid.NewGuid());
        var applicationId = new ApplicationId(Guid.NewGuid());
        var templateId = new TemplateId(Guid.NewGuid());
        var createdOn = DateTime.UtcNow;

        // Act
        var contributor = _userFactory.CreateContributor(
            userId,
            roleId,
            "John Doe",
            "john@example.com",
            createdBy,
            applicationId,
            "APP-12345",
            templateId,
            createdOn);

        // Assert
        Assert.NotNull(contributor);
        Assert.Equal(userId, contributor.Id);
        Assert.Equal(roleId, contributor.RoleId);
        Assert.Equal("John Doe", contributor.Name);
        Assert.Equal("john@example.com", contributor.Email);
        Assert.Equal(createdOn, contributor.CreatedOn);
        Assert.Equal(createdBy, contributor.CreatedBy);

        Assert.Contains(contributor.Permissions, p =>
            p.ResourceType == ResourceType.User
            && p.ResourceKey == "john@example.com"
            && p.AccessType == AccessType.Read);

        Assert.Contains(contributor.Permissions, p =>
            p.ResourceType == ResourceType.Application
            && p.ResourceKey == applicationId.Value.ToString()
            && p.AccessType == AccessType.Write);

        Assert.Contains(contributor.Permissions, p =>
            p.ResourceType == ResourceType.Template
            && p.ResourceKey == templateId.Value.ToString()
            && p.AccessType == AccessType.Read);

        Assert.DoesNotContain(contributor.Permissions, p =>
            p.ResourceType == ResourceType.Template
            && p.AccessType == AccessType.Write);

        // Check that domain event was raised (permissions will be added in the event handler)
        var domainEvents = contributor.DomainEvents.ToList();
        Assert.Single(domainEvents);
        var contributorAddedEvent = domainEvents.First() as ContributorAddedEvent;
        Assert.NotNull(contributorAddedEvent);
        Assert.Equal(applicationId, contributorAddedEvent.ApplicationId);
        Assert.Equal("APP-12345", contributorAddedEvent.ApplicationReference);
        Assert.Equal(templateId, contributorAddedEvent.TemplateId);
        Assert.Equal(contributor, contributorAddedEvent.Contributor);
        Assert.Equal(createdBy, contributorAddedEvent.AddedBy);
        Assert.Equal(createdOn, contributorAddedEvent.AddedOn);
    }

    [Fact]
    public void CreateContributor_WithNullId_ShouldThrowArgumentException()
    {
        // Arrange
        var roleId = new RoleId(Guid.NewGuid());
        var createdBy = new UserId(Guid.NewGuid());
        var applicationId = new ApplicationId(Guid.NewGuid());
        var templateId = new TemplateId(Guid.NewGuid());

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _userFactory.CreateContributor(
            null!,
            roleId,
            "John Doe",
            "john@example.com",
            createdBy,
            applicationId,
            "APP-12345",
            templateId));

        Assert.Contains("Id cannot be null", exception.Message);
    }

    [Fact]
    public void CreateContributor_WithNullTemplateId_ShouldThrowArgumentException()
    {
        // Arrange
        var userId = new UserId(Guid.NewGuid());
        var roleId = new RoleId(Guid.NewGuid());
        var createdBy = new UserId(Guid.NewGuid());
        var applicationId = new ApplicationId(Guid.NewGuid());

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _userFactory.CreateContributor(
            userId,
            roleId,
            "John Doe",
            "john@example.com",
            createdBy,
            applicationId,
            "APP-12345",
            null!));

        Assert.Contains("TemplateId cannot be null", exception.Message);
    }

    [Fact]
    public void AddTemplatePermissionToUser_WithValidParameters_ShouldAddTemplatePermissions()
    {
        // Arrange
        var user = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(Guid.NewGuid()),
            "Test User",
            "test@example.com",
            DateTime.UtcNow,
            null,
            null,
            null);

        var templateId = Guid.NewGuid().ToString();
        var grantedBy = new UserId(Guid.NewGuid());
        var grantedOn = DateTime.UtcNow;

        // Act
        _userFactory.AddTemplatePermissionToUser(
            user,
            templateId,
            new[] { AccessType.Read, AccessType.Write },
            grantedBy,
            grantedOn);

        // Assert
        var templateGrants = user.Permissions.Where(p => p.ResourceType == ResourceType.Template).ToList();
        Assert.Equal(2, templateGrants.Count);
        Assert.Contains(templateGrants, p => p.AccessType == AccessType.Read);
        Assert.Contains(templateGrants, p => p.AccessType == AccessType.Write);
        Assert.All(templateGrants, p => Assert.Equal(grantedBy, p.GrantedBy));
        Assert.All(templateGrants, p => Assert.Equal(grantedOn, p.GrantedOn));
        Assert.All(templateGrants, p => Assert.Equal(templateId, p.ResourceKey));
    }

    [Fact]
    public void AddTemplatePermissionToUser_WithNullUser_ShouldThrowArgumentException()
    {
        // Arrange
        var templateId = "template-123";
        var grantedBy = new UserId(Guid.NewGuid());

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _userFactory.AddTemplatePermissionToUser(
            null!,
            templateId,
            new[] { AccessType.Read },
            grantedBy));

        Assert.Contains("User cannot be null", exception.Message);
    }

    [Fact]
    public void AddTemplatePermissionToUser_WithEmptyTemplateId_ShouldThrowArgumentException()
    {
        // Arrange
        var user = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(Guid.NewGuid()),
            "Test User",
            "test@example.com",
            DateTime.UtcNow,
            null,
            null,
            null);

        var grantedBy = new UserId(Guid.NewGuid());

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _userFactory.AddTemplatePermissionToUser(
            user,
            "",
            new[] { AccessType.Read },
            grantedBy));

        Assert.Contains("ResourceKey cannot be null or empty", exception.Message);
    }

    [Fact]
    public void AddPermissionToUser_WithValidParameters_ShouldAddPermissions()
    {
        // Arrange
        var user = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(Guid.NewGuid()),
            "Test User",
            "test@example.com",
            DateTime.UtcNow,
            null,
            null,
            null);

        var resourceKey = "app-123";
        var applicationId = new ApplicationId(Guid.NewGuid());
        var grantedBy = new UserId(Guid.NewGuid());
        var grantedOn = DateTime.UtcNow;

        // Act
        _userFactory.AddPermissionToUser(
            user,
            resourceKey,
            ResourceType.Application,
            new[] { AccessType.Read, AccessType.Write },
            grantedBy,
            applicationId,
            grantedOn);

        // Assert
        Assert.Equal(2, user.Permissions.Count);
        Assert.Contains(user.Permissions, p => p.AccessType == AccessType.Read);
        Assert.Contains(user.Permissions, p => p.AccessType == AccessType.Write);
        Assert.All(user.Permissions, p => Assert.Equal(applicationId, p.ApplicationId));
        Assert.All(user.Permissions, p => Assert.Equal(grantedBy, p.GrantedBy));
        Assert.All(user.Permissions, p => Assert.Equal(grantedOn, p.GrantedOn));
    }

    [Fact]
    public void CreateStandardUser_ShouldGrantTemplateWriteWithoutApplicationAnyPermissions()
    {
        var templateId = new TemplateId(Guid.NewGuid());
        var grantedBy = new UserId(Guid.NewGuid());

        var user = _userFactory.CreateStandardUser(
            new UserId(Guid.NewGuid()),
            "New User",
            "new.user@example.com",
            [templateId],
            grantedBy);

        Assert.Contains(user.Permissions, p =>
            p.ResourceType == ResourceType.Template
            && p.ResourceKey == templateId.Value.ToString()
            && p.AccessType == AccessType.Write);
        Assert.Contains(user.Permissions, p =>
            p.ResourceType == ResourceType.Template
            && p.ResourceKey == templateId.Value.ToString()
            && p.AccessType == AccessType.Read);
        Assert.DoesNotContain(user.Permissions, p =>
            p.ResourceType == ResourceType.Application
            && p.ResourceKey == PermissionConstants.AnyResourceKey);
    }

    [Fact]
    public void CreateUser_ShouldGrantTemplateWriteWithoutApplicationAnyPermissions()
    {
        var templateId = new TemplateId(Guid.NewGuid());

        var user = _userFactory.CreateUser(
            new UserId(Guid.NewGuid()),
            new RoleId(RoleConstants.UserRoleId),
            "Registered User",
            "registered.user@example.com",
            templateId);

        Assert.Contains(user.Permissions, p =>
            p.ResourceType == ResourceType.Template
            && p.ResourceKey == templateId.Value.ToString()
            && p.AccessType == AccessType.Write);
        Assert.DoesNotContain(user.Permissions, p =>
            p.ResourceType == ResourceType.Application
            && p.ResourceKey == PermissionConstants.AnyResourceKey);
    }

    [Fact]
    public void CreateUser_WithTenantId_ShouldPrefixNotificationPermissionKeys()
    {
        var tenantId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var user = _userFactory.CreateUser(
            new UserId(Guid.NewGuid()),
            new RoleId(RoleConstants.UserRoleId),
            "Registered User",
            "registered.user@example.com",
            tenantId: tenantId);

        Assert.Contains(user.Permissions, p =>
            p.ResourceType == ResourceType.Notifications
            && p.ResourceKey == "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee:registered.user@example.com"
            && p.AccessType == AccessType.Read);
        Assert.DoesNotContain(user.Permissions, p =>
            p.ResourceType == ResourceType.Notifications
            && p.ResourceKey == "registered.user@example.com");
    }
} 