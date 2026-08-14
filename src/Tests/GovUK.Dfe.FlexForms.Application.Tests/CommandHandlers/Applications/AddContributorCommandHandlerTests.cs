using GovUK.Dfe.FlexForms.Application.Applications.Commands;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MockQueryable.NSubstitute;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Security.Claims;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.Applications;

public class AddContributorCommandHandlerTests
{
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    private static ITenantContextAccessor CreateContributorTenantContext()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var tenant = new TenantConfiguration(TestTenantId, "Test", config, Array.Empty<string>());
        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.CurrentTenant.Returns(tenant);
        return accessor;
    }

    private static ITenantMembershipService CreateContributorMembershipService()
    {
        var service = Substitute.For<ITenantMembershipService>();
        service.GetActiveMembershipAsync(Arg.Any<Guid>(), Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns((TenantMembership?)null);
        service.UpsertMembershipAsync(Arg.Any<Guid>(), Arg.Any<UserId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new TenantMembership(
                new TenantMembershipId(Guid.NewGuid()),
                ci.ArgAt<Guid>(0),
                ci.ArgAt<UserId>(1),
                new RoleId(Guid.NewGuid()),
                DateTime.UtcNow));
        return service;
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldAddNewContributor_WhenValidRequest(
        AddContributorCommand command,
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

        // Mock template version with template ID
        var templateVersion = new TemplateVersion(
            new TemplateVersionId(Guid.NewGuid()),
            new TemplateId(Guid.NewGuid()),
            "1.0.0",
            "{}",
            DateTime.UtcNow,
            user.Id!);
        application.GetType().GetProperty("TemplateVersion")?.SetValue(application, templateVersion);

        var users = new[] { user }.AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        applicationRepo.Query().Returns(applications);

        permissionCheckerService.IsApplicationOwner(application, user.Id!.Value.ToString()).Returns(true);
        permissionCheckerService.IsAdmin().Returns(false);

        var cacheInvalidator = Substitute.For<IUserCacheInvalidator>();
        var membershipService = CreateContributorMembershipService();

        // Mock the CreateContributor method
        var contributor = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(RoleConstants.UserRoleId),
            command.Name,
            command.Email,
            DateTime.UtcNow,
            user.Id!,
            null,
            null);

        userFactory.CreateContributor(
            Arg.Any<UserId>(),
            Arg.Any<RoleId>(),
            command.Name,
            command.Email,
            user.Id!,
            new ApplicationId(command.ApplicationId),
            application.ApplicationReference,
            templateVersion.TemplateId,
            Arg.Any<DateTime>(),
            Arg.Any<Guid?>())
            .Returns(contributor);

        var handler = new AddContributorCommandHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            userFactory,
            cacheInvalidator,
            CreateContributorTenantContext(),
            membershipService,
            unitOfWork);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess, $"Result was not successful. Error: {result.Error}");
        await membershipService.Received(1).UpsertMembershipAsync(
            TestTenantId,
            contributor.Id!,
            RoleNames.User,
            Arg.Any<CancellationToken>());
        Assert.NotNull(result.Value);
        Assert.Equal(contributor.Id!.Value, result.Value.UserId);
        Assert.Equal(command.Name, result.Value.Name);
        Assert.Equal(command.Email, result.Value.Email);
        Assert.Equal(contributor.RoleId.Value, result.Value.RoleId);

        await userRepo.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await cacheInvalidator.Received(1).InvalidateForUserAsync(contributor.Email, Arg.Any<string?>(), contributor.Id!, Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldAddExistingContributorWithMissingPermissions_WhenContributorExists(
        AddContributorCommand command,
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

        // Mock template version with template ID
        var templateVersion = new TemplateVersion(
            new TemplateVersionId(Guid.NewGuid()),
            new TemplateId(Guid.NewGuid()),
            "1.0.0",
            "{}",
            DateTime.UtcNow,
            user.Id!);
        application.GetType().GetProperty("TemplateVersion")?.SetValue(application, templateVersion);

        // Existing contributor with only Read permission
        var existingContributor = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(RoleConstants.UserRoleId),
            command.Name,
            command.Email,
            DateTime.UtcNow,
            null,
            null,
            null);

        // Add only Read permission using real factory
        var realUserFactory = new UserFactory();
        realUserFactory.AddPermissionToUser(
            existingContributor,
            command.ApplicationId.ToString(),
            ResourceType.Application,
            new[] { AccessType.Read },
            user.Id!,
            new ApplicationId(command.ApplicationId),
            DateTime.UtcNow);

        var users = new[] { user, existingContributor }.AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        applicationRepo.Query().Returns(applications);

        permissionCheckerService.IsApplicationOwner(application, user.Id!.Value.ToString()).Returns(true);
        permissionCheckerService.IsAdmin().Returns(false);

        var cacheInvalidator = Substitute.For<IUserCacheInvalidator>();
        var membershipService = CreateContributorMembershipService();

        var handler = new AddContributorCommandHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            userFactory,
            cacheInvalidator,
            CreateContributorTenantContext(),
            membershipService,
            unitOfWork);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess, $"Result was not successful. Error: {result.Error}");
        Assert.NotNull(result.Value);
        await membershipService.Received(1).UpsertMembershipAsync(
            TestTenantId,
            existingContributor.Id!,
            RoleNames.User,
            Arg.Any<CancellationToken>());
        Assert.Equal(existingContributor.Id!.Value, result.Value.UserId);
        Assert.Equal(command.Name, result.Value.Name);
        Assert.Equal(command.Email, result.Value.Email);

        // Verify that all required permissions were added to existing contributor
        
        // Application permissions
        userFactory.Received(1).AddPermissionToUser(
            existingContributor,
            command.ApplicationId.ToString(),
            ResourceType.Application,
            Arg.Is<AccessType[]>(a => a.Length == 2 && a.Contains(AccessType.Read) && a.Contains(AccessType.Write)),
            user.Id!,
            new ApplicationId(command.ApplicationId),
            Arg.Any<DateTime>());

        // Application files permissions  
        userFactory.Received(1).AddPermissionToUser(
            existingContributor,
            command.ApplicationId.ToString(),
            ResourceType.ApplicationFiles,
            Arg.Is<AccessType[]>(a => a.Length == 3 && a.Contains(AccessType.Read) && a.Contains(AccessType.Write) && a.Contains(AccessType.Delete)),
            user.Id!,
            new ApplicationId(command.ApplicationId),
            Arg.Any<DateTime>());

        // Notifications permissions
        userFactory.Received(1).AddPermissionToUser(
            existingContributor,
            TenantScopedIdentityKey.Combine(TestTenantId, existingContributor.Email),
            ResourceType.Notifications,
            Arg.Is<AccessType[]>(a => a.Length == 3 && a.Contains(AccessType.Read) && a.Contains(AccessType.Write) && a.Contains(AccessType.Delete)),
            user.Id!,
            new ApplicationId(command.ApplicationId),
            Arg.Any<DateTime>());

        // Template permissions
        userFactory.Received(1).AddTemplatePermissionToUser(
            existingContributor,
            templateVersion.TemplateId.Value.ToString(),
            Arg.Is<AccessType[]>(a => a.Length == 2 && a.Contains(AccessType.Read) && a.Contains(AccessType.Write)),
            user.Id!,
            Arg.Any<DateTime>());

        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await cacheInvalidator.Received(1).InvalidateForUserAsync(existingContributor.Email, Arg.Any<string?>(), existingContributor.Id!, Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldReturnExistingContributor_WhenContributorExistsWithAllPermissions(
        AddContributorCommand command,
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

        // Mock template version with template ID
        var templateVersion = new TemplateVersion(
            new TemplateVersionId(Guid.NewGuid()),
            new TemplateId(Guid.NewGuid()),
            "1.0.0",
            "{}",
            DateTime.UtcNow,
            user.Id!);
        application.GetType().GetProperty("TemplateVersion")?.SetValue(application, templateVersion);

        // Existing contributor with both Read and Write permissions
        var existingContributor = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(RoleConstants.UserRoleId),
            command.Name,
            command.Email,
            DateTime.UtcNow,
            null,
            null,
            null);

        // Add both Read and Write permissions using real factory
        var realUserFactory = new UserFactory();
        realUserFactory.AddPermissionToUser(
            existingContributor,
            command.ApplicationId.ToString(),
            ResourceType.Application,
            new[] { AccessType.Read, AccessType.Write },
            user.Id!,
            new ApplicationId(command.ApplicationId),
            DateTime.UtcNow);

        // Add ApplicationFiles permissions as well
        realUserFactory.AddPermissionToUser(
            existingContributor,
            command.ApplicationId.ToString(),
            ResourceType.ApplicationFiles,
            new[] { AccessType.Read, AccessType.Write },
            user.Id!,
            new ApplicationId(command.ApplicationId),
            DateTime.UtcNow);

        var users = new[] { user, existingContributor }.AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        applicationRepo.Query().Returns(applications);

        permissionCheckerService.IsApplicationOwner(application, user.Id!.Value.ToString()).Returns(true);
        permissionCheckerService.IsAdmin().Returns(false);

        var handler = new AddContributorCommandHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            userFactory,
            Substitute.For<IUserCacheInvalidator>(),
            CreateContributorTenantContext(),
            CreateContributorMembershipService(),
            unitOfWork);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess, $"Result was not successful. Error: {result.Error}");
        Assert.NotNull(result.Value);
        Assert.Equal(existingContributor.Id!.Value, result.Value.UserId);
        Assert.Equal(command.Name, result.Value.Name);
        Assert.Equal(command.Email, result.Value.Email);

        // Verify that all permission methods were called (but since the user already has all permissions, they're idempotent and won't add duplicates)
        
        // Application permissions
        userFactory.Received(1).AddPermissionToUser(
            existingContributor,
            command.ApplicationId.ToString(),
            ResourceType.Application,
            Arg.Any<AccessType[]>(),
            user.Id!,
            new ApplicationId(command.ApplicationId),
            Arg.Any<DateTime>());

        // Application files permissions  
        userFactory.Received(1).AddPermissionToUser(
            existingContributor,
            command.ApplicationId.ToString(),
            ResourceType.ApplicationFiles,
            Arg.Any<AccessType[]>(),
            user.Id!,
            new ApplicationId(command.ApplicationId),
            Arg.Any<DateTime>());

        // Notifications permissions
        userFactory.Received(1).AddPermissionToUser(
            existingContributor,
            TenantScopedIdentityKey.Combine(TestTenantId, existingContributor.Email),
            ResourceType.Notifications,
            Arg.Any<AccessType[]>(),
            user.Id!,
            new ApplicationId(command.ApplicationId),
            Arg.Any<DateTime>());

        // Template permissions
        userFactory.Received(1).AddTemplatePermissionToUser(
            existingContributor,
            templateVersion.TemplateId.Value.ToString(),
            Arg.Any<AccessType[]>(),
            user.Id!,
            Arg.Any<DateTime>());

        // CommitAsync should still be called
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldReturnFailure_WhenUserNotAuthenticated(
        AddContributorCommand command,
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

        var handler = new AddContributorCommandHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            userFactory,
            Substitute.For<IUserCacheInvalidator>(),
            CreateContributorTenantContext(),
            CreateContributorMembershipService(),
            unitOfWork);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Not authenticated", result.Error);

        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldReturnFailure_WhenNoUserIdentifier(
        AddContributorCommand command,
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

        var handler = new AddContributorCommandHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            userFactory,
            Substitute.For<IUserCacheInvalidator>(),
            CreateContributorTenantContext(),
            CreateContributorMembershipService(),
            unitOfWork);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("No user identifier", result.Error);

        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldReturnFailure_WhenUserNotFound(
        AddContributorCommand command,
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

        var handler = new AddContributorCommandHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            userFactory,
            Substitute.For<IUserCacheInvalidator>(),
            CreateContributorTenantContext(),
            CreateContributorMembershipService(),
            unitOfWork);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("User not found", result.Error);

        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldReturnFailure_WhenApplicationNotFound(
        AddContributorCommand command,
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

        var handler = new AddContributorCommandHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            userFactory,
            Substitute.For<IUserCacheInvalidator>(),
            CreateContributorTenantContext(),
            CreateContributorMembershipService(),
            unitOfWork);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Application not found", result.Error);

        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldReturnFailure_WhenUserDoesNotHavePermission(
        AddContributorCommand command,
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

        var handler = new AddContributorCommandHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            userFactory,
            Substitute.For<IUserCacheInvalidator>(),
            CreateContributorTenantContext(),
            CreateContributorMembershipService(),
            unitOfWork);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Only the application owner or admin can add contributors", result.Error);

        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldReturnFailure_WhenHttpContextIsNull(
        AddContributorCommand command,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IEaRepository<User> userRepo,
        IPermissionCheckerService permissionCheckerService,
        IUserFactory userFactory,
        IUnitOfWork unitOfWork)
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns((HttpContext?)null);

        var handler = new AddContributorCommandHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            userFactory,
            Substitute.For<IUserCacheInvalidator>(),
            CreateContributorTenantContext(),
            CreateContributorMembershipService(),
            unitOfWork);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Not authenticated", result.Error);

        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldReturnFailure_WhenUserIdentityNotAuthenticated(
        AddContributorCommand command,
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
            new(ClaimTypes.Email, "test@example.com")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims)); // Not authenticated
        httpContextAccessor.HttpContext.Returns(httpContext);

        var handler = new AddContributorCommandHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            userFactory,
            Substitute.For<IUserCacheInvalidator>(),
            CreateContributorTenantContext(),
            CreateContributorMembershipService(),
            unitOfWork);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Not authenticated", result.Error);

        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldReturnFailure_WhenExceptionThrown(
        AddContributorCommand command,
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

        // Mock to throw exception
        userRepo.Query().Throws(new InvalidOperationException("Database error"));

        var handler = new AddContributorCommandHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            userFactory,
            Substitute.For<IUserCacheInvalidator>(),
            CreateContributorTenantContext(),
            CreateContributorMembershipService(),
            unitOfWork);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Database error", result.Error);

        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldUseAppIdClaim_WhenEmailClaimNotAvailable(
        AddContributorCommand command,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IEaRepository<User> userRepo,
        IPermissionCheckerService permissionCheckerService,
        IUnitOfWork unitOfWork,
        IUserFactory userFactory)
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        var externalId = "external-user-id";
        var claims = new List<Claim>
        {
            new("appid", externalId)
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        httpContextAccessor.HttpContext.Returns(httpContext);

        var user = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(Guid.NewGuid()),
            "Test User",
            "test@example.com",
            DateTime.UtcNow,
            null,
            null,
            null,
            externalId);

        var application = new Domain.Entities.Application(
            new ApplicationId(command.ApplicationId),
            "APP-001",
            new TemplateVersionId(Guid.NewGuid()),
            DateTime.UtcNow,
            user.Id!);

        // Mock template version with template ID
        var templateVersion = new TemplateVersion(
            new TemplateVersionId(Guid.NewGuid()),
            new TemplateId(Guid.NewGuid()),
            "1.0.0",
            "{}",
            DateTime.UtcNow,
            user.Id!);
        application.GetType().GetProperty("TemplateVersion")?.SetValue(application, templateVersion);

        var users = new[] { user }.AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        applicationRepo.Query().Returns(applications);

        permissionCheckerService.IsApplicationOwner(application, user.Id!.Value.ToString()).Returns(true);
        permissionCheckerService.IsAdmin().Returns(false);

        // Mock the CreateContributor method
        var contributor = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(RoleConstants.UserRoleId),
            command.Name,
            command.Email,
            DateTime.UtcNow,
            user.Id!,
            null,
            null);

        userFactory.CreateContributor(
            Arg.Any<UserId>(),
            Arg.Any<RoleId>(),
            command.Name,
            command.Email,
            user.Id!,
            new ApplicationId(command.ApplicationId),
            application.ApplicationReference,
            templateVersion.TemplateId,
            Arg.Any<DateTime>(),
            Arg.Any<Guid?>())
            .Returns(contributor);

        var handler = new AddContributorCommandHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            userFactory,
            Substitute.For<IUserCacheInvalidator>(),
            CreateContributorTenantContext(),
            CreateContributorMembershipService(),
            unitOfWork);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess, $"Result was not successful. Error: {result.Error}");
        Assert.NotNull(result.Value);
        Assert.Equal(contributor.Id!.Value, result.Value.UserId);

        await userRepo.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldUseAzpClaim_WhenAppIdClaimNotAvailable(
        AddContributorCommand command,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IEaRepository<User> userRepo,
        IPermissionCheckerService permissionCheckerService,
        IUnitOfWork unitOfWork,
        IUserFactory userFactory)
    {
        // Arrange
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        var externalId = "external-user-id";
        var claims = new List<Claim>
        {
            new("azp", externalId)
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        httpContextAccessor.HttpContext.Returns(httpContext);

        var user = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(Guid.NewGuid()),
            "Test User",
            "test@example.com",
            DateTime.UtcNow,
            null,
            null,
            null,
            externalId);

        var application = new Domain.Entities.Application(
            new ApplicationId(command.ApplicationId),
            "APP-001",
            new TemplateVersionId(Guid.NewGuid()),
            DateTime.UtcNow,
            user.Id!);

        // Mock template version with template ID
        var templateVersion = new TemplateVersion(
            new TemplateVersionId(Guid.NewGuid()),
            new TemplateId(Guid.NewGuid()),
            "1.0.0",
            "{}",
            DateTime.UtcNow,
            user.Id!);
        application.GetType().GetProperty("TemplateVersion")?.SetValue(application, templateVersion);

        var users = new[] { user }.AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        applicationRepo.Query().Returns(applications);

        permissionCheckerService.IsApplicationOwner(application, user.Id!.Value.ToString()).Returns(true);
        permissionCheckerService.IsAdmin().Returns(false);

        // Mock the CreateContributor method
        var contributor = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(RoleConstants.UserRoleId),
            command.Name,
            command.Email,
            DateTime.UtcNow,
            user.Id!,
            null,
            null);

        userFactory.CreateContributor(
            Arg.Any<UserId>(),
            Arg.Any<RoleId>(),
            command.Name,
            command.Email,
            user.Id!,
            new ApplicationId(command.ApplicationId),
            application.ApplicationReference,
            templateVersion.TemplateId,
            Arg.Any<DateTime>(),
            Arg.Any<Guid?>())
            .Returns(contributor);

        var handler = new AddContributorCommandHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            userFactory,
            Substitute.For<IUserCacheInvalidator>(),
            CreateContributorTenantContext(),
            CreateContributorMembershipService(),
            unitOfWork);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess, $"Result was not successful. Error: {result.Error}");
        Assert.NotNull(result.Value);
        Assert.Equal(contributor.Id!.Value, result.Value.UserId);

        await userRepo.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldReturnFailure_WhenAdminUserDoesNotHavePermission(
        AddContributorCommand command,
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

        var handler = new AddContributorCommandHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            userFactory,
            Substitute.For<IUserCacheInvalidator>(),
            CreateContributorTenantContext(),
            CreateContributorMembershipService(),
            unitOfWork);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Only the application owner or admin can add contributors", result.Error);

        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldSucceed_WhenAdminUserHasPermission(
        AddContributorCommand command,
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

        // Mock template version with template ID
        var templateVersion = new TemplateVersion(
            new TemplateVersionId(Guid.NewGuid()),
            new TemplateId(Guid.NewGuid()),
            "1.0.0",
            "{}",
            DateTime.UtcNow,
            user.Id!);
        application.GetType().GetProperty("TemplateVersion")?.SetValue(application, templateVersion);

        var users = new[] { user }.AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        applicationRepo.Query().Returns(applications);

        permissionCheckerService.IsApplicationOwner(application, user.Id!.Value.ToString()).Returns(false);
        permissionCheckerService.IsAdmin().Returns(true);

        // Mock the CreateContributor method
        var contributor = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(RoleConstants.UserRoleId),
            command.Name,
            command.Email,
            DateTime.UtcNow,
            user.Id!,
            null,
            null);

        userFactory.CreateContributor(
            Arg.Any<UserId>(),
            Arg.Any<RoleId>(),
            command.Name,
            command.Email,
            user.Id!,
            new ApplicationId(command.ApplicationId),
            application.ApplicationReference,
            templateVersion.TemplateId,
            Arg.Any<DateTime>(),
            Arg.Any<Guid?>())
            .Returns(contributor);

        var handler = new AddContributorCommandHandler(
            applicationRepo,
            userRepo,
            httpContextAccessor,
            permissionCheckerService,
            userFactory,
            Substitute.For<IUserCacheInvalidator>(),
            CreateContributorTenantContext(),
            CreateContributorMembershipService(),
            unitOfWork);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess, $"Result was not successful. Error: {result.Error}");
        Assert.NotNull(result.Value);
        Assert.Equal(contributor.Id!.Value, result.Value.UserId);
        Assert.Equal(command.Name, result.Value.Name);
        Assert.Equal(command.Email, result.Value.Email);

        await userRepo.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }
} 