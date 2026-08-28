using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.FlexForms.Application.Applications.Queries;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;
using Microsoft.AspNetCore.Http;
using MockQueryable.NSubstitute;
using NSubstitute;
using System.Reflection;
using System.Security.Claims;
using GovUK.Dfe.FlexForms.Domain.Factories;
using NSubstitute.ExceptionExtensions;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryHandlers.Applications;

public class GetContributorsForApplicationQueryHandlerTests
{
    private static ITenantPermissionFilter CreateTenantPermissionFilter(bool belongsToTenant = true)
    {
        var filter = Substitute.For<ITenantPermissionFilter>();
        filter.ApplicationBelongsToCurrentTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(belongsToTenant);
        return filter;
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization), typeof(PermissionCustomization))]
    public async Task Handle_ShouldReturnContributors_WhenValidRequestWithAppId(
        GetContributorsForApplicationQuery query,
        User user,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IEaRepository<User> userRepo,
        IPermissionCheckerService permissionCheckerService)
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        var externalId = "external-id";
        var claims = new List<Claim>
        {
            new("appid", externalId),
            new(ClaimTypes.Email, "test@example.com")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        httpContextAccessor.HttpContext.Returns(httpContext);

        // Create application
        var applicationId = new ApplicationId(query.ApplicationId);
        var templateVersionId = new TemplateVersionId(Guid.NewGuid());
        var application = new Domain.Entities.Application(
            applicationId,
            "APP-001",
            templateVersionId,
            DateTime.UtcNow,
            user.Id!);

        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        applicationRepo.Query().Returns(applications);

        // Set the user's external provider ID to match the app ID in claims
        user.GetType().GetProperty("ExternalProviderId")?.SetValue(user, externalId);

        // Create contributors
        var contributor1 = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(Guid.NewGuid()),
            "Contributor 1",
            "contributor1@example.com",
            DateTime.UtcNow,
            null, null, null, null,
            new List<Permission>());

        var contributor2 = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(Guid.NewGuid()),
            "Contributor 2",
            "contributor2@example.com",
            DateTime.UtcNow,
            null, null, null, null,
            new List<Permission>());

        // Add permissions to contributors using factory
        var userFactory = new UserFactory();
        userFactory.AddPermissionToUser(contributor1, applicationId.Value.ToString(), ResourceType.Application, new[] { AccessType.Read }, user.Id!, applicationId, DateTime.UtcNow);
        userFactory.AddPermissionToUser(contributor2, applicationId.Value.ToString(), ResourceType.Application, new[] { AccessType.Write }, user.Id!, applicationId, DateTime.UtcNow);

        // Set up Application property on permissions
        foreach (var contributor in new[] { contributor1, contributor2 })
        {
            foreach (var permission in contributor.Permissions.Where(p => p.ApplicationId == applicationId))
            {
                permission.GetType().GetProperty("Application")?.SetValue(permission, application);
            }
        }

        var users = new[] { user, contributor1, contributor2 }.AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        // Mock permission checks
        permissionCheckerService.IsApplicationOwner(application, user.Id!.Value.ToString()).Returns(true);
        permissionCheckerService.IsAdmin().Returns(false);

        var handler = new GetContributorsForApplicationQueryHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            CreateTenantPermissionFilter());

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Value.Count);
        Assert.Contains(result.Value, c => c.Name == "Contributor 1");
        Assert.Contains(result.Value, c => c.Name == "Contributor 2");
        Assert.DoesNotContain(result.Value, c => c.Name == user.Name); // Creator should be excluded
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization), typeof(PermissionCustomization))]
    public async Task Handle_ShouldReturnContributorsWithPermissionDetails_WhenIncludePermissionDetailsIsTrue(
        GetContributorsForApplicationQuery query,
        User user,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IEaRepository<User> userRepo,
        IPermissionCheckerService permissionCheckerService)
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        var email = "test@example.com";
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, email)
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        httpContextAccessor.HttpContext.Returns(httpContext);

        // Create application
        var applicationId = new ApplicationId(query.ApplicationId);
        var templateVersionId = new TemplateVersionId(Guid.NewGuid());
        var application = new Domain.Entities.Application(
            applicationId,
            "APP-001",
            templateVersionId,
            DateTime.UtcNow,
            user.Id!);

        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        applicationRepo.Query().Returns(applications);

        // Set the user's email to match the email in claims
        user.GetType().GetProperty("Email")?.SetValue(user, email);

        // Create contributor with permissions
        var contributor = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(Guid.NewGuid()),
            "Contributor",
            "contributor@example.com",
            DateTime.UtcNow,
            null, null, null, null,
            new List<Permission>());

        // Add permissions to contributor using factory
        var userFactory = new UserFactory();
        userFactory.AddPermissionToUser(contributor, "test-resource", ResourceType.Application, new[] { AccessType.Read }, user.Id!, applicationId, DateTime.UtcNow);

        // Set up Application property on permissions
        foreach (var permission in contributor.Permissions.Where(p => p.ApplicationId == applicationId))
        {
            permission.GetType().GetProperty("Application")?.SetValue(permission, application);
        }

        var users = new[] { user, contributor }.AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        // Mock permission checks
        permissionCheckerService.IsApplicationOwner(application, user.Id!.Value.ToString()).Returns(true);
        permissionCheckerService.IsAdmin().Returns(false);

        var queryWithPermissionDetails = new GetContributorsForApplicationQuery(query.ApplicationId, true);
        var handler = new GetContributorsForApplicationQueryHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            CreateTenantPermissionFilter());

        // Act
        var result = await handler.Handle(queryWithPermissionDetails, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Single(result.Value);
        var contributorDto = result.Value.First();
        Assert.NotNull(contributorDto.Authorization);
        Assert.Single(contributorDto.Authorization.Permissions);
        Assert.Equal(applicationId.Value, contributorDto.Authorization.Permissions.First().ApplicationId);
        Assert.Equal(ResourceType.Application, contributorDto.Authorization.Permissions.First().ResourceType);
        Assert.Equal("test-resource", contributorDto.Authorization.Permissions.First().ResourceKey);
        Assert.Equal(AccessType.Read, contributorDto.Authorization.Permissions.First().AccessType);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_ShouldReturnUnauthorized_WhenUserNotAuthenticated(
        GetContributorsForApplicationQuery query,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IEaRepository<User> userRepo,
        IPermissionCheckerService permissionCheckerService)
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity()); // Not authenticated
        httpContextAccessor.HttpContext.Returns(httpContext);

        var handler = new GetContributorsForApplicationQueryHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            CreateTenantPermissionFilter());

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Not authenticated", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_ShouldReturnNoUserIdentifier_WhenNoUserIdentifierFound(
        GetContributorsForApplicationQuery query,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IEaRepository<User> userRepo,
        IPermissionCheckerService permissionCheckerService)
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new("some-other-claim", "value")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        httpContextAccessor.HttpContext.Returns(httpContext);

        var handler = new GetContributorsForApplicationQueryHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            CreateTenantPermissionFilter());

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("No user identifier", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_ShouldReturnUserNotFound_WhenUserDoesNotExist(
        GetContributorsForApplicationQuery query,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IEaRepository<User> userRepo,
        IPermissionCheckerService permissionCheckerService)
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        var email = "nonexistent@example.com";
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, email)
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        httpContextAccessor.HttpContext.Returns(httpContext);

        var users = Array.Empty<User>().AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        var handler = new GetContributorsForApplicationQueryHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            CreateTenantPermissionFilter());

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("User not found", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_ShouldReturnApplicationNotFound_WhenApplicationDoesNotExist(
        GetContributorsForApplicationQuery query,
        User user,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IEaRepository<User> userRepo,
        IPermissionCheckerService permissionCheckerService)
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        var email = "test@example.com";
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, email)
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        httpContextAccessor.HttpContext.Returns(httpContext);

        // Set the user's email to match the email in claims
        user.GetType().GetProperty("Email")?.SetValue(user, email);
        
        var users = new[] { user }.AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        var applications = Array.Empty<Domain.Entities.Application>().AsQueryable().BuildMockDbSet();
        applicationRepo.Query().Returns(applications);

        var handler = new GetContributorsForApplicationQueryHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            CreateTenantPermissionFilter());

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Application not found", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_ShouldReturnForbidden_WhenUserIsNotOwnerOrAdmin(
        GetContributorsForApplicationQuery query,
        User user,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IEaRepository<User> userRepo,
        IPermissionCheckerService permissionCheckerService)
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        var email = "test@example.com";
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, email)
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        httpContextAccessor.HttpContext.Returns(httpContext);

        // Create application owned by different user
        var applicationId = new ApplicationId(query.ApplicationId);
        var templateVersionId = new TemplateVersionId(Guid.NewGuid());
        var otherUserId = new UserId(Guid.NewGuid());
        var application = new Domain.Entities.Application(
            applicationId,
            "APP-001",
            templateVersionId,
            DateTime.UtcNow,
            otherUserId);

        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        applicationRepo.Query().Returns(applications);

        // Set the user's email to match the email in claims
        user.GetType().GetProperty("Email")?.SetValue(user, email);
        
        var users = new[] { user }.AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        // Mock permission checks - user is neither owner nor admin
        permissionCheckerService.IsApplicationOwner(application, user.Id!.Value.ToString()).Returns(false);
        permissionCheckerService.IsAdmin().Returns(false);

        var handler = new GetContributorsForApplicationQueryHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            CreateTenantPermissionFilter());

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Only the application owner or admin can view contributors", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_ShouldReturnContributors_WhenUserIsAdmin(
        GetContributorsForApplicationQuery query,
        User user,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IEaRepository<User> userRepo,
        IPermissionCheckerService permissionCheckerService)
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        var email = "admin@example.com";
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, email)
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        httpContextAccessor.HttpContext.Returns(httpContext);

        // Create application owned by different user
        var applicationId = new ApplicationId(query.ApplicationId);
        var templateVersionId = new TemplateVersionId(Guid.NewGuid());
        var otherUserId = new UserId(Guid.NewGuid());
        var application = new Domain.Entities.Application(
            applicationId,
            "APP-001",
            templateVersionId,
            DateTime.UtcNow,
            otherUserId);

        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        applicationRepo.Query().Returns(applications);

        // Set the user's email to match the email in claims
        user.GetType().GetProperty("Email")?.SetValue(user, email);
        
        var users = new[] { user }.AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        // Mock permission checks - user is admin but not owner
        permissionCheckerService.IsApplicationOwner(application, user.Id!.Value.ToString()).Returns(false);
        permissionCheckerService.IsAdmin().Returns(true);

        var handler = new GetContributorsForApplicationQueryHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            CreateTenantPermissionFilter());

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        // Should return empty list since no contributors exist
        Assert.Empty(result.Value);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_ShouldReturnContributors_WhenUserIsOwner(
        GetContributorsForApplicationQuery query,
        User user,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IEaRepository<User> userRepo,
        IPermissionCheckerService permissionCheckerService)
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        var email = "owner@example.com";
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, email)
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        httpContextAccessor.HttpContext.Returns(httpContext);

        // Create application owned by the user
        var applicationId = new ApplicationId(query.ApplicationId);
        var templateVersionId = new TemplateVersionId(Guid.NewGuid());
        var application = new Domain.Entities.Application(
            applicationId,
            "APP-001",
            templateVersionId,
            DateTime.UtcNow,
            user.Id!);

        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        applicationRepo.Query().Returns(applications);

        // Set the user's email to match the email in claims
        user.GetType().GetProperty("Email")?.SetValue(user, email);
        
        var users = new[] { user }.AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        // Mock permission checks - user is owner but not admin
        permissionCheckerService.IsApplicationOwner(application, user.Id!.Value.ToString()).Returns(true);
        permissionCheckerService.IsAdmin().Returns(false);

        var handler = new GetContributorsForApplicationQueryHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            CreateTenantPermissionFilter());

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        // Should return empty list since no contributors exist
        Assert.Empty(result.Value);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_ShouldHandleException_WhenExceptionOccurs(
        GetContributorsForApplicationQuery query,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IEaRepository<User> userRepo,
        IPermissionCheckerService permissionCheckerService)
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        var email = "test@example.com";
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, email)
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        httpContextAccessor.HttpContext.Returns(httpContext);

        // Mock repository to throw exception
        userRepo.Query().Throws(new InvalidOperationException("Database error"));

        var handler = new GetContributorsForApplicationQueryHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            CreateTenantPermissionFilter());

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Database error", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_ShouldReturnEmptyList_WhenNoContributorsExist(
        GetContributorsForApplicationQuery query,
        User user,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IEaRepository<User> userRepo,
        IPermissionCheckerService permissionCheckerService)
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        var email = "test@example.com";
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, email)
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        httpContextAccessor.HttpContext.Returns(httpContext);

        // Create application
        var applicationId = new ApplicationId(query.ApplicationId);
        var templateVersionId = new TemplateVersionId(Guid.NewGuid());
        var application = new Domain.Entities.Application(
            applicationId,
            "APP-001",
            templateVersionId,
            DateTime.UtcNow,
            user.Id!);

        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        applicationRepo.Query().Returns(applications);

        // Set the user's email to match the email in claims
        user.GetType().GetProperty("Email")?.SetValue(user, email);
        
        // Only the creator exists, no contributors
        var users = new[] { user }.AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        // Mock permission checks
        permissionCheckerService.IsApplicationOwner(application, user.Id!.Value.ToString()).Returns(true);
        permissionCheckerService.IsAdmin().Returns(false);

        var handler = new GetContributorsForApplicationQueryHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            CreateTenantPermissionFilter());

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value);
    }
} 