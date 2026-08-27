using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.FlexForms.Application.Applications.Commands;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using MediatR;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;
using Microsoft.EntityFrameworkCore;
using MockQueryable.NSubstitute;
using GovUK.Dfe.FlexForms.Domain.Factories;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.Applications;

public class RemoveContributorCommandHandlerTests
{
    private static ITenantPermissionFilter CreateTenantPermissionFilter(bool belongsToTenant = true)
    {
        var filter = Substitute.For<ITenantPermissionFilter>();
        filter.ApplicationBelongsToCurrentTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(belongsToTenant);
        return filter;
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldRemoveContributor_WhenValidRequest(
        RemoveContributorCommand command,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IEaRepository<User> userRepo,
        IPermissionCheckerService permissionCheckerService,
        IUnitOfWork unitOfWork,
        IUserFactory userFactory)
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

        var user = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(Guid.NewGuid()),
            "Test User",
            email,
            DateTime.UtcNow,
            null,
            null,
            null);

        var application = new Domain.Entities.Application(
            new ApplicationId(command.ApplicationId),
            "APP-001",
            new TemplateVersionId(Guid.NewGuid()),
            DateTime.UtcNow,
            user.Id!);

        // Contributor with permission for this application
        var contributor = new User(
            new UserId(command.UserId),
            new RoleId(Guid.NewGuid()),
            "Contributor User",
            "contributor@example.com",
            DateTime.UtcNow,
            null,
            null,
            null);

        // Add permission for this application using real factory
        var realUserFactory = new UserFactory();
        realUserFactory.AddPermissionToUser(
            contributor,
            command.ApplicationId.ToString(),
            ResourceType.Application,
            new[] { AccessType.Read },
            user.Id!,
            new ApplicationId(command.ApplicationId),
            DateTime.UtcNow);

        // Create a mock query that returns different results based on the query
        // First query: find current user by email (returns user)
        // Second query: get contributor with all permissions (returns contributor)
        var allUsers = new[] { user, contributor }.AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(allUsers);

        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        applicationRepo.Query().Returns(applications);

        permissionCheckerService.IsApplicationOwner(application, user.Id!.Value.ToString()).Returns(true);
        permissionCheckerService.IsAdmin().Returns(false);

        // Mock the RemovePermissionFromUser method to return true
        userFactory.RemovePermissionFromUser(Arg.Any<User>(), Arg.Any<Permission>()).Returns(true);

        var handler = new RemoveContributorCommandHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            userFactory,
            Substitute.For<IUserCacheInvalidator>(),
            CreateTenantPermissionFilter(),
            unitOfWork);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess, $"Result was not successful. Error: {result.Error}");
        Assert.True(result.Value);

        await unitOfWork.Received(1)
            .CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldReturnFailure_WhenUserNotAuthenticated(
        RemoveContributorCommand command,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IEaRepository<User> userRepo,
        IPermissionCheckerService permissionCheckerService,
        IUserFactory userFactory,
        IUnitOfWork unitOfWork)
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        httpContextAccessor.HttpContext.Returns(httpContext);

        var handler = new RemoveContributorCommandHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            userFactory,
            Substitute.For<IUserCacheInvalidator>(),
            CreateTenantPermissionFilter(),
            unitOfWork);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Not authenticated", result.Error);

        await unitOfWork.DidNotReceive()
            .CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldReturnFailure_WhenNoUserIdentifier(
        RemoveContributorCommand command,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IEaRepository<User> userRepo,
        IPermissionCheckerService permissionCheckerService,
        IUserFactory userFactory,
        IUnitOfWork unitOfWork)
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new("someclaim", "value")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        httpContextAccessor.HttpContext.Returns(httpContext);

        var handler = new RemoveContributorCommandHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            userFactory,
            Substitute.For<IUserCacheInvalidator>(),
            CreateTenantPermissionFilter(),
            unitOfWork);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("No user identifier", result.Error);

        await unitOfWork.DidNotReceive()
            .CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldReturnFailure_WhenUserNotFound(
        RemoveContributorCommand command,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IEaRepository<User> userRepo,
        IPermissionCheckerService permissionCheckerService,
        IUserFactory userFactory,
        IUnitOfWork unitOfWork)
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

        // No users found
        var users = new List<User>().AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        var handler = new RemoveContributorCommandHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            userFactory,
            Substitute.For<IUserCacheInvalidator>(),
            CreateTenantPermissionFilter(),
            unitOfWork);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("User not found", result.Error);

        await unitOfWork.DidNotReceive()
            .CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldReturnFailure_WhenUserDoesNotHavePermission(
        RemoveContributorCommand command,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IEaRepository<User> userRepo,
        IPermissionCheckerService permissionCheckerService,
        IUserFactory userFactory,
        IUnitOfWork unitOfWork)
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

        var user = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(Guid.NewGuid()),
            "Test User",
            email,
            DateTime.UtcNow,
            null,
            null,
            null);

        var application = new Domain.Entities.Application(
            new ApplicationId(command.ApplicationId),
            "APP-001",
            new TemplateVersionId(Guid.NewGuid()),
            DateTime.UtcNow,
            user.Id!);

        var users = new[] { user }.AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        applicationRepo.Query().Returns(applications);

        permissionCheckerService.IsApplicationOwner(application, user.Id!.Value.ToString()).Returns(false);
        permissionCheckerService.IsAdmin().Returns(false);

        var handler = new RemoveContributorCommandHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            userFactory,
            Substitute.For<IUserCacheInvalidator>(),
            CreateTenantPermissionFilter(),
            unitOfWork);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Only the application owner or admin can remove contributors", result.Error);

        await unitOfWork.DidNotReceive()
            .CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldReturnFailure_WhenApplicationNotFound(
        RemoveContributorCommand command,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IEaRepository<User> userRepo,
        IPermissionCheckerService permissionCheckerService,
        IUserFactory userFactory,
        IUnitOfWork unitOfWork)
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

        var user = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(Guid.NewGuid()),
            "Test User",
            email,
            DateTime.UtcNow,
            null,
            null,
            null);

        var users = new[] { user }.AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        // No application found
        var applications = new List<Domain.Entities.Application>().AsQueryable().BuildMockDbSet();
        applicationRepo.Query().Returns(applications);

        var handler = new RemoveContributorCommandHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            userFactory,
            Substitute.For<IUserCacheInvalidator>(),
            CreateTenantPermissionFilter(),
            unitOfWork);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Application not found", result.Error);

        await unitOfWork.DidNotReceive()
            .CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldReturnFailure_WhenContributorNotFound(
        RemoveContributorCommand command,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IEaRepository<User> userRepo,
        IPermissionCheckerService permissionCheckerService,
        IUserFactory userFactory,
        IUnitOfWork unitOfWork)
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

        var user = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(Guid.NewGuid()),
            "Test User",
            email,
            DateTime.UtcNow,
            null,
            null,
            null);

        var application = new Domain.Entities.Application(
            new ApplicationId(command.ApplicationId),
            "APP-001",
            new TemplateVersionId(Guid.NewGuid()),
            DateTime.UtcNow,
            user.Id!);

        var users = new[] { user }.AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        applicationRepo.Query().Returns(applications);

        permissionCheckerService.IsApplicationOwner(application, user.Id!.Value.ToString()).Returns(true);
        permissionCheckerService.IsAdmin().Returns(false);

        var handler = new RemoveContributorCommandHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            userFactory,
            Substitute.For<IUserCacheInvalidator>(),
            CreateTenantPermissionFilter(),
            unitOfWork);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Contributor not found", result.Error);

        await unitOfWork.DidNotReceive()
            .CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldReturnFailure_WhenContributorDoesNotHavePermission(
        RemoveContributorCommand command,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IEaRepository<User> userRepo,
        IPermissionCheckerService permissionCheckerService,
        IUserFactory userFactory,
        IUnitOfWork unitOfWork)
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

        var user = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(Guid.NewGuid()),
            "Test User",
            email,
            DateTime.UtcNow,
            null,
            null,
            null);

        var application = new Domain.Entities.Application(
            new ApplicationId(command.ApplicationId),
            "APP-001",
            new TemplateVersionId(Guid.NewGuid()),
            DateTime.UtcNow,
            user.Id!);

        // Contributor without permission for this application
        var contributor = new User(
            new UserId(command.UserId),
            new RoleId(Guid.NewGuid()),
            "Contributor User",
            "contributor@example.com",
            DateTime.UtcNow,
            null,
            null,
            null);

        // Create a mock query that returns different results based on the query
        // First query: find current user by email (returns user)
        // Second query: get contributor with all permissions (returns contributor without permissions)
        var allUsers = new[] { user, contributor }.AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(allUsers);

        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        applicationRepo.Query().Returns(applications);

        permissionCheckerService.IsApplicationOwner(application, user.Id!.Value.ToString()).Returns(true);
        permissionCheckerService.IsAdmin().Returns(false);

        var handler = new RemoveContributorCommandHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            userFactory,
            Substitute.For<IUserCacheInvalidator>(),
            CreateTenantPermissionFilter(),
            unitOfWork);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Contributor does not have permission for this application", result.Error);

        await unitOfWork.DidNotReceive()
            .CommitAsync(Arg.Any<CancellationToken>());
    }
} 