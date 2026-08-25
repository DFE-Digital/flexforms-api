using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.FlexForms.Application.Applications.Commands;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Tests.Helpers;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;
using MockQueryable.NSubstitute;
using NSubstitute;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.Applications;

public class DeleteApplicationCommandHandlerTests
{
    private static ITenantTemplateResolver AllowAllTenantTemplates()
    {
        var resolver = Substitute.For<ITenantTemplateResolver>();
        resolver.IsTemplateInCurrentTenantAsync(Arg.Any<TemplateId>(), Arg.Any<CancellationToken>())
            .Returns(true);
        return resolver;
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_ShouldDeleteApplication_WhenValidRequest(
        DeleteApplicationCommand command,
        User user,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IPermissionCheckerService permissionCheckerService,
        IUnitOfWork unitOfWork)
    {
        var externalId = "test-app-id";
        var userWithExternalId = new User(
            user.Id!,
            user.RoleId,
            user.Name,
            user.Email,
            user.CreatedOn,
            user.CreatedBy,
            user.LastModifiedOn,
            user.LastModifiedBy,
            externalId);

        var applicationId = new ApplicationId(command.ApplicationId);
        var templateVersionId = new TemplateVersionId(Guid.NewGuid());
        var application = new Domain.Entities.Application(
            applicationId,
            "APP-001",
            templateVersionId,
            DateTime.UtcNow,
            userWithExternalId.Id!,
            ApplicationStatus.InProgress);

        var templateVersion = new TemplateVersion(
            templateVersionId,
            new TemplateId(Guid.NewGuid()),
            "1.0.0",
            "{}",
            DateTime.UtcNow,
            userWithExternalId.Id!);
        application.GetType().GetProperty("TemplateVersion")?.SetValue(application, templateVersion);

        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        applicationRepo.Query().Returns(applications);

        permissionCheckerService.HasPermission(
            ResourceType.Application,
            command.ApplicationId.ToString(),
            AccessType.Write)
            .Returns(true);

        var cacheInvalidator = Substitute.For<IUserCacheInvalidator>();
        var handler = CreateHandler(
            applicationRepo,
            AuthenticatedUserServiceTestHelper.MockReturningUser(userWithExternalId),
            permissionCheckerService,
            unitOfWork,
            cacheInvalidator: cacheInvalidator);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(command.ApplicationId, result.Value.ApplicationId);
        Assert.Equal(ApplicationStatus.Deleted, result.Value.Status);
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await cacheInvalidator.Received(1).InvalidateApplicationListingsAsync(Arg.Any<CancellationToken>());
        await cacheInvalidator.Received(1).InvalidateForUserAsync(
            userWithExternalId.Email,
            userWithExternalId.ExternalProviderId,
            userWithExternalId.Id!,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_ShouldDeleteApplication_WhenValidRequestWithEmail(
        DeleteApplicationCommand command,
        User user,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IPermissionCheckerService permissionCheckerService,
        IUnitOfWork unitOfWork)
    {
        var email = "test@example.com";
        var testUser = new User(
            user.Id!,
            user.RoleId,
            user.Name,
            email,
            user.CreatedOn,
            user.CreatedBy,
            user.LastModifiedOn,
            user.LastModifiedBy);

        var applicationId = new ApplicationId(command.ApplicationId);
        var templateVersionId = new TemplateVersionId(Guid.NewGuid());
        var application = new Domain.Entities.Application(
            applicationId,
            "APP-001",
            templateVersionId,
            DateTime.UtcNow,
            testUser.Id!,
            ApplicationStatus.InProgress);

        var templateVersion = new TemplateVersion(
            templateVersionId,
            new TemplateId(Guid.NewGuid()),
            "1.0.0",
            "{}",
            DateTime.UtcNow,
            testUser.Id!);
        application.GetType().GetProperty("TemplateVersion")?.SetValue(application, templateVersion);

        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        applicationRepo.Query().Returns(applications);

        permissionCheckerService.HasPermission(
            ResourceType.Application,
            command.ApplicationId.ToString(),
            AccessType.Write)
            .Returns(true);

        var cacheInvalidator = Substitute.For<IUserCacheInvalidator>();
        var handler = CreateHandler(
            applicationRepo,
            AuthenticatedUserServiceTestHelper.MockReturningUser(testUser),
            permissionCheckerService,
            unitOfWork,
            cacheInvalidator: cacheInvalidator);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(ApplicationStatus.Deleted, result.Value.Status);
        await cacheInvalidator.Received(1).InvalidateApplicationListingsAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldReturnUnauthorized_WhenUserNotAuthenticated(
        DeleteApplicationCommand command,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IPermissionCheckerService permissionCheckerService,
        IUnitOfWork unitOfWork)
    {
        permissionCheckerService.HasPermission(
            Arg.Any<ResourceType>(),
            Arg.Any<string>(),
            Arg.Any<AccessType>())
            .Returns(false);

        var handler = CreateHandler(
            applicationRepo,
            AuthenticatedUserServiceTestHelper.MockReturning(Result<User>.Forbid("Not authenticated")),
            permissionCheckerService,
            unitOfWork);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Not authenticated", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldReturnApplicationNotFound_WhenApplicationDoesNotExist(
        DeleteApplicationCommand command,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IPermissionCheckerService permissionCheckerService,
        IUnitOfWork unitOfWork)
    {
        var user = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(Guid.NewGuid()),
            "Test User",
            "test@example.com",
            DateTime.UtcNow,
            null,
            null,
            null);

        var applications = Array.Empty<Domain.Entities.Application>().AsQueryable().BuildMockDbSet();
        applicationRepo.Query().Returns(applications);

        permissionCheckerService.HasPermission(
            ResourceType.Application,
            command.ApplicationId.ToString(),
            AccessType.Write)
            .Returns(true);

        var handler = CreateHandler(
            applicationRepo,
            AuthenticatedUserServiceTestHelper.MockReturningUser(user),
            permissionCheckerService,
            unitOfWork);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Application not found", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_ShouldReturnForbidden_WhenUserHasNoPermission(
        DeleteApplicationCommand command,
        User user,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IPermissionCheckerService permissionCheckerService,
        IUnitOfWork unitOfWork)
    {
        var email = "test@example.com";
        var testUser = new User(
            user.Id!,
            user.RoleId,
            user.Name,
            email,
            user.CreatedOn,
            user.CreatedBy,
            user.LastModifiedOn,
            user.LastModifiedBy);

        var applicationId = new ApplicationId(command.ApplicationId);
        var application = new Domain.Entities.Application(
            applicationId,
            "APP-001",
            new TemplateVersionId(Guid.NewGuid()),
            DateTime.UtcNow,
            testUser.Id!,
            ApplicationStatus.InProgress);

        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        applicationRepo.Query().Returns(applications);

        permissionCheckerService.HasPermission(
            ResourceType.Application,
            command.ApplicationId.ToString(),
            AccessType.Write)
            .Returns(false);

        var handler = CreateHandler(
            applicationRepo,
            AuthenticatedUserServiceTestHelper.MockReturningUser(testUser),
            permissionCheckerService,
            unitOfWork);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("User does not have permission to delete this application", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_ShouldReturnError_WhenApplicationAlreadyDeleted(
        DeleteApplicationCommand command,
        User user,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IPermissionCheckerService permissionCheckerService,
        IUnitOfWork unitOfWork)
    {
        var email = "test@example.com";
        var testUser = new User(
            user.Id!,
            user.RoleId,
            user.Name,
            email,
            user.CreatedOn,
            user.CreatedBy,
            user.LastModifiedOn,
            user.LastModifiedBy);

        var applicationId = new ApplicationId(command.ApplicationId);
        var templateVersionId = new TemplateVersionId(Guid.NewGuid());
        var application = new Domain.Entities.Application(
            applicationId,
            "APP-001",
            templateVersionId,
            DateTime.UtcNow,
            testUser.Id!,
            ApplicationStatus.Deleted);

        var templateVersion = new TemplateVersion(
            templateVersionId,
            new TemplateId(Guid.NewGuid()),
            "1.0.0",
            "{}",
            DateTime.UtcNow,
            testUser.Id!);
        application.GetType().GetProperty("TemplateVersion")?.SetValue(application, templateVersion);

        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        applicationRepo.Query().Returns(applications);

        permissionCheckerService.HasPermission(
            ResourceType.Application,
            command.ApplicationId.ToString(),
            AccessType.Write)
            .Returns(true);

        var handler = CreateHandler(
            applicationRepo,
            AuthenticatedUserServiceTestHelper.MockReturningUser(testUser),
            permissionCheckerService,
            unitOfWork);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Application has already been deleted", result.Error!);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_ShouldReturnForbidden_WhenApplicationBelongsToAnotherTenant(
        DeleteApplicationCommand command,
        User user,
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IPermissionCheckerService permissionCheckerService,
        IUnitOfWork unitOfWork)
    {
        var testUser = new User(
            user.Id!,
            user.RoleId,
            user.Name,
            "visits-admin@example.com",
            user.CreatedOn,
            user.CreatedBy,
            user.LastModifiedOn,
            user.LastModifiedBy);

        var applicationId = new ApplicationId(command.ApplicationId);
        var templateVersionId = new TemplateVersionId(Guid.NewGuid());
        var transferTemplateId = new TemplateId(Guid.NewGuid());
        var application = new Domain.Entities.Application(
            applicationId,
            "APP-TRANSFER-001",
            templateVersionId,
            DateTime.UtcNow,
            testUser.Id!,
            ApplicationStatus.InProgress);

        var templateVersion = new TemplateVersion(
            templateVersionId,
            transferTemplateId,
            "1.0.0",
            "{}",
            DateTime.UtcNow,
            testUser.Id!);
        application.GetType().GetProperty("TemplateVersion")?.SetValue(application, templateVersion);

        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        applicationRepo.Query().Returns(applications);

        permissionCheckerService.HasPermission(
            ResourceType.Application,
            command.ApplicationId.ToString(),
            AccessType.Write)
            .Returns(true);

        var tenantTemplateResolver = Substitute.For<ITenantTemplateResolver>();
        tenantTemplateResolver.IsTemplateInCurrentTenantAsync(transferTemplateId, Arg.Any<CancellationToken>())
            .Returns(false);

        var cacheInvalidator = Substitute.For<IUserCacheInvalidator>();
        var handler = CreateHandler(
            applicationRepo,
            AuthenticatedUserServiceTestHelper.MockReturningUser(testUser),
            permissionCheckerService,
            unitOfWork,
            tenantTemplateResolver,
            cacheInvalidator);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Application does not belong to the current tenant", result.Error);
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await cacheInvalidator.DidNotReceive().InvalidateApplicationListingsAsync(Arg.Any<CancellationToken>());
    }

    private static DeleteApplicationCommandHandler CreateHandler(
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IAuthenticatedUserService authenticatedUserService,
        IPermissionCheckerService permissionCheckerService,
        IUnitOfWork unitOfWork,
        ITenantTemplateResolver? tenantTemplateResolver = null,
        IUserCacheInvalidator? cacheInvalidator = null)
    {
        return new DeleteApplicationCommandHandler(
            applicationRepo,
            authenticatedUserService,
            permissionCheckerService,
            tenantTemplateResolver ?? AllowAllTenantTemplates(),
            cacheInvalidator ?? Substitute.For<IUserCacheInvalidator>(),
            unitOfWork);
    }
}
