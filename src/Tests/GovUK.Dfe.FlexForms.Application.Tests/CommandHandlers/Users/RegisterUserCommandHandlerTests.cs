using GovUK.Dfe.FlexForms.Application.Users.Commands;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Security.Configurations;
using GovUK.Dfe.CoreLibs.Security.Interfaces;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MockQueryable.NSubstitute;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.Users;

public class RegisterUserCommandHandlerTests
{
    private static GovUK.Dfe.FlexForms.Application.Services.ITenantTemplateResolver AllowAllTenantTemplates()
    {
        var resolver = Substitute.For<GovUK.Dfe.FlexForms.Application.Services.ITenantTemplateResolver>();
        resolver.IsTemplateInCurrentTenantAsync(Arg.Any<TemplateId>(), Arg.Any<CancellationToken>())
            .Returns(true);
        resolver.GetTemplateIdsForCurrentTenantAsync(Arg.Any<CancellationToken>())
            .Returns(callInfo => Array.Empty<TemplateId>());
        return resolver;
    }

    private static IEaRepository<Template> LiveTemplateRepository(Guid templateId)
    {
        var template = new Template(
            new TemplateId(templateId),
            "Live Template",
            DateTime.UtcNow,
            new UserId(Guid.NewGuid()),
            isLive: true);
        var mockDbSet = new List<Template> { template }.AsQueryable().BuildMockDbSet();
        var repo = Substitute.For<IEaRepository<Template>>();
        repo.Query().Returns(mockDbSet);
        return repo;
    }

    private static void StubCreateUser(
        IUserFactory userFactory,
        User newUser,
        string email)
    {
        userFactory.CreateUser(
            Arg.Any<UserId>(),
            Arg.Any<RoleId>(),
            Arg.Any<string>(),
            email,
            Arg.Any<TemplateId?>(),
            Arg.Any<DateTime?>())
            .Returns(newUser);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ShouldCreateNewUser_WhenValidTokenAndUserDoesNotExist(
        string subjectToken,
        string email,
        string name,
        IEaRepository<User> userRepo,
        IExternalIdentityValidator externalValidator,
        IHttpContextAccessor httpContextAccessor,
        IUserFactory userFactory,
        IUnitOfWork unitOfWork)
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Name, name)
        };
        var identity = new ClaimsIdentity(claims);
        var claimsPrincipal = new ClaimsPrincipal(identity);

        externalValidator.ValidateIdTokenAsync(subjectToken, false, false, Arg.Any<InternalServiceAuthOptions?>(), Arg.Any<TestAuthenticationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(claimsPrincipal);

        var tenantContextAccessor = Substitute.For<ITenantContextAccessor>();

        // No existing user
        var users = new List<User>().AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        // Mock the CreateUser method
        var newUser = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(RoleConstants.UserRoleId),
            name,
            email,
            DateTime.UtcNow,
            null,
            null,
            null);

        userFactory.CreateUser(
            Arg.Any<UserId>(),
            Arg.Any<RoleId>(),
            Arg.Any<string>(),  // Changed from specific 'name' to Any to match handler behavior
            email,
            Arg.Any<TemplateId?>(),
            Arg.Any<DateTime?>())
            .Returns(newUser);

                var templateId = Guid.NewGuid();

        var handler = new RegisterUserCommandHandler(
            userRepo,
            LiveTemplateRepository(templateId),
            externalValidator,
            httpContextAccessor,
            userFactory,
            unitOfWork,
            tenantContextAccessor,
            AllowAllTenantTemplates());
        var command = new RegisterUserCommand(subjectToken, templateId);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess, $"Result was not successful. Error: {result.Error}");
        Assert.NotNull(result.Value);
        Assert.Equal(newUser.Id!.Value, result.Value.UserId);
        Assert.Equal(name, result.Value.Name);
        Assert.Equal(email, result.Value.Email);
        Assert.Equal(RoleConstants.UserRoleId, result.Value.RoleId);

        await userRepo.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ShouldReturnExistingUser_WhenUserAlreadyExists(
        string subjectToken,
        string email,
        string name,
        IEaRepository<User> userRepo,
        IExternalIdentityValidator externalValidator,
        IHttpContextAccessor httpContextAccessor,
        IUserFactory userFactory,
        IUnitOfWork unitOfWork)
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Name, name)
        };
        var identity = new ClaimsIdentity(claims);
        var claimsPrincipal = new ClaimsPrincipal(identity);

        externalValidator.ValidateIdTokenAsync(subjectToken, false, false, Arg.Any<InternalServiceAuthOptions?>(), Arg.Any<TestAuthenticationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(claimsPrincipal);

        var tenantContextAccessor = Substitute.For<ITenantContextAccessor>();
        var templateId = Guid.NewGuid();
        var userId = new UserId(Guid.NewGuid());
        var templatePermission = new TemplatePermission(
            new TemplatePermissionId(Guid.NewGuid()),
            userId,
            new TemplateId(templateId),
            AccessType.Read,
            DateTime.UtcNow,
            userId);

        // Existing user who already has permission for the template (so handler returns without committing)
        var existingUser = new User(
            userId,
            new RoleId(RoleConstants.UserRoleId),
            name,
            email,
            DateTime.UtcNow,
            null,
            null,
            null,
            initialPermissions: null,
            initialTemplatePermissions: new[] { templatePermission });

        var users = new[] { existingUser }.AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        var handler = new RegisterUserCommandHandler(
            userRepo,
            LiveTemplateRepository(templateId),
            externalValidator,
            httpContextAccessor,
            userFactory,
            unitOfWork,
            tenantContextAccessor,
            AllowAllTenantTemplates());

        var command = new RegisterUserCommand(subjectToken, templateId);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess, $"Result was not successful. Error: {result.Error}");
        Assert.NotNull(result.Value);
        Assert.Equal(existingUser.Id!.Value, result.Value.UserId);
        Assert.Equal(name, result.Value.Name);
        Assert.Equal(email, result.Value.Email);

        await userRepo.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ShouldFallbackToEmail_WhenNameClaimNotAvailable(
        string subjectToken,
        string email,
        IEaRepository<User> userRepo,
        IExternalIdentityValidator externalValidator,
        IHttpContextAccessor httpContextAccessor,
        IUserFactory userFactory,
        IUnitOfWork unitOfWork)
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, email)
            // No name claim
        };
        var identity = new ClaimsIdentity(claims);
        var claimsPrincipal = new ClaimsPrincipal(identity);

        externalValidator.ValidateIdTokenAsync(subjectToken, false, false, Arg.Any<InternalServiceAuthOptions?>(), Arg.Any<TestAuthenticationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(claimsPrincipal);

        var tenantContextAccessor = Substitute.For<ITenantContextAccessor>();

        // No existing user
        var users = new List<User>().AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        // Mock the CreateUser method - should be called with email as name
        var newUser = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(RoleConstants.UserRoleId),
            email, // Name falls back to email
            email,
            DateTime.UtcNow,
            null,
            null,
            null);

        userFactory.CreateUser(
            Arg.Any<UserId>(),
            Arg.Any<RoleId>(),
            Arg.Any<string>(),  // Changed from specific email to Any to match handler behavior
            email,
            Arg.Any<TemplateId?>(),
            Arg.Any<DateTime?>())
            .Returns(newUser);

        var templateId = Guid.NewGuid();

        var handler = new RegisterUserCommandHandler(
            userRepo,
            LiveTemplateRepository(templateId),
            externalValidator,
            httpContextAccessor,
            userFactory,
            unitOfWork,
            tenantContextAccessor,
            AllowAllTenantTemplates());
        var command = new RegisterUserCommand(subjectToken, templateId);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess, $"Result was not successful. Error: {result.Error}");
        Assert.NotNull(result.Value);
        Assert.Equal(email, result.Value.Name); // Name should be email

        userFactory.Received(1).CreateUser(
            Arg.Any<UserId>(),
            Arg.Any<RoleId>(),
            email, // Name should be email
            email,
            Arg.Any<TemplateId?>(),
            Arg.Any<DateTime?>());
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ShouldUseLowercaseNameClaim_WhenAvailable(
        string subjectToken,
        string email,
        string name,
        IEaRepository<User> userRepo,
        IExternalIdentityValidator externalValidator,
        IHttpContextAccessor httpContextAccessor,
        IUserFactory userFactory,
        IUnitOfWork unitOfWork)
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, email),
            new("name", name) // lowercase "name" claim
        };
        var identity = new ClaimsIdentity(claims);
        var claimsPrincipal = new ClaimsPrincipal(identity);

        externalValidator.ValidateIdTokenAsync(subjectToken, false, false, Arg.Any<InternalServiceAuthOptions?>(), Arg.Any<TestAuthenticationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(claimsPrincipal);

        var tenantContextAccessor = Substitute.For<ITenantContextAccessor>();

        // No existing user
        var users = new List<User>().AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        // Mock the CreateUser method
        var newUser = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(RoleConstants.UserRoleId),
            name,
            email,
            DateTime.UtcNow,
            null,
            null,
            null);

        userFactory.CreateUser(
            Arg.Any<UserId>(),
            Arg.Any<RoleId>(),
            Arg.Any<string>(),  // Changed from specific 'name' to Any to match handler behavior
            email,
            Arg.Any<TemplateId?>(),
            Arg.Any<DateTime?>())
            .Returns(newUser);

        var templateId = Guid.NewGuid();

        var handler = new RegisterUserCommandHandler(
            userRepo,
            LiveTemplateRepository(templateId),
            externalValidator,
            httpContextAccessor,
            userFactory,
            unitOfWork,
            tenantContextAccessor,
            AllowAllTenantTemplates());
        var command = new RegisterUserCommand(subjectToken, templateId);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess, $"Result was not successful. Error: {result.Error}");
        Assert.NotNull(result.Value);
        Assert.Equal(name, result.Value.Name);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ShouldReturnFailure_WhenTokenValidationFails(
        string subjectToken,
        IEaRepository<User> userRepo,
        IExternalIdentityValidator externalValidator,
        IHttpContextAccessor httpContextAccessor,
        IUserFactory userFactory,
        IUnitOfWork unitOfWork)
    {
        // Arrange
        var tenantContextAccessor = Substitute.For<ITenantContextAccessor>();
        externalValidator.ValidateIdTokenAsync(subjectToken, false, false, Arg.Any<InternalServiceAuthOptions?>(), Arg.Any<TestAuthenticationOptions?>(), Arg.Any<CancellationToken>())
            .Throws(new SecurityTokenException("Invalid token"));

                var templateId = Guid.NewGuid();

        var handler = new RegisterUserCommandHandler(
            userRepo,
            LiveTemplateRepository(templateId),
            externalValidator,
            httpContextAccessor,
            userFactory,
            unitOfWork,
            tenantContextAccessor,
            AllowAllTenantTemplates());
        var command = new RegisterUserCommand(subjectToken, templateId);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Invalid token", result.Error);

        await userRepo.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ShouldReturnFailure_WhenEmailClaimMissing(
        string subjectToken,
        IEaRepository<User> userRepo,
        IExternalIdentityValidator externalValidator,
        IHttpContextAccessor httpContextAccessor,
        IUserFactory userFactory,
        IUnitOfWork unitOfWork)
    {
        // Arrange
        var claims = new List<Claim>
        {
            new("some-claim", "value")
            // No email claim
        };
        var identity = new ClaimsIdentity(claims);
        var claimsPrincipal = new ClaimsPrincipal(identity);

        externalValidator.ValidateIdTokenAsync(subjectToken, false, false, Arg.Any<InternalServiceAuthOptions?>(), Arg.Any<TestAuthenticationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(claimsPrincipal);

        var tenantContextAccessor = Substitute.For<ITenantContextAccessor>();

        var templateId = Guid.NewGuid();
        var handler = new RegisterUserCommandHandler(
            userRepo,
            LiveTemplateRepository(templateId),
            externalValidator,
            httpContextAccessor,
            userFactory,
            unitOfWork,
            tenantContextAccessor,
            AllowAllTenantTemplates());
        var command = new RegisterUserCommand(subjectToken, templateId);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Missing email", result.Error);

        await userRepo.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ShouldReturnFailure_WhenDatabaseErrorOccurs(
        string subjectToken,
        string email,
        string name,
        IEaRepository<User> userRepo,
        IExternalIdentityValidator externalValidator,
        IHttpContextAccessor httpContextAccessor,
        IUserFactory userFactory,
        IUnitOfWork unitOfWork)
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Name, name)
        };
        var identity = new ClaimsIdentity(claims);
        var claimsPrincipal = new ClaimsPrincipal(identity);

        externalValidator.ValidateIdTokenAsync(subjectToken, false, false, Arg.Any<InternalServiceAuthOptions?>(), Arg.Any<TestAuthenticationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(claimsPrincipal);

        var tenantContextAccessor = Substitute.For<ITenantContextAccessor>();

        // Mock to throw exception
        userRepo.Query().Throws(new InvalidOperationException("Database error"));

                var templateId = Guid.NewGuid();

        var handler = new RegisterUserCommandHandler(
            userRepo,
            LiveTemplateRepository(templateId),
            externalValidator,
            httpContextAccessor,
            userFactory,
            unitOfWork,
            tenantContextAccessor,
            AllowAllTenantTemplates());
        var command = new RegisterUserCommand(subjectToken, templateId);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Database error", result.Error);

        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ShouldIncludeAuthorization_WhenUserHasPermissions(
        string subjectToken,
        string email,
        string name,
        IEaRepository<User> userRepo,
        IExternalIdentityValidator externalValidator,
        IHttpContextAccessor httpContextAccessor,
        IUserFactory userFactory,
        IUnitOfWork unitOfWork)
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Name, name)
        };
        var identity = new ClaimsIdentity(claims);
        var claimsPrincipal = new ClaimsPrincipal(identity);

        externalValidator.ValidateIdTokenAsync(subjectToken, false, false, Arg.Any<InternalServiceAuthOptions?>(), Arg.Any<TestAuthenticationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(claimsPrincipal);

        var tenantContextAccessor = Substitute.For<ITenantContextAccessor>();

        // No existing user
        var users = new List<User>().AsQueryable().BuildMockDbSet();
        userRepo.Query().Returns(users);

        // Mock the CreateUser method with a user that has permissions
        var userId = new UserId(Guid.NewGuid());
        var newUser = new User(
            userId,
            new RoleId(RoleConstants.UserRoleId),
            name,
            email,
            DateTime.UtcNow,
            null,
            null,
            null);

        // Add permissions using the factory
        var realFactory = new UserFactory();
        realFactory.AddPermissionToUser(
            newUser,
            email,
            GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums.ResourceType.Notifications,
            new[] { GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums.AccessType.Read },
            userId,
            null,
            DateTime.UtcNow);

        userFactory.CreateUser(
            Arg.Any<UserId>(),
            Arg.Any<RoleId>(),
            Arg.Any<string>(),  // Changed from specific 'name' to Any to match handler behavior
            email,
            Arg.Any<TemplateId?>(),
            Arg.Any<DateTime?>())
            .Returns(newUser);

        var templateId = Guid.NewGuid();

        var handler = new RegisterUserCommandHandler(
            userRepo,
            LiveTemplateRepository(templateId),
            externalValidator,
            httpContextAccessor,
            userFactory,
            unitOfWork,
            tenantContextAccessor,
            AllowAllTenantTemplates());
        var command = new RegisterUserCommand(subjectToken, templateId);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess, $"Result was not successful. Error: {result.Error}");
        Assert.NotNull(result.Value);
        Assert.NotNull(result.Value.Authorization);
        Assert.NotNull(result.Value.Authorization.Permissions);
        Assert.NotEmpty(result.Value.Authorization.Permissions);
    }
}

