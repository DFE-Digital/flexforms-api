using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.FlexForms.Application.Applications.Commands;
using GovUK.Dfe.FlexForms.Application.Services;
using File = GovUK.Dfe.FlexForms.Domain.Entities.File;
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

public class SubmitApplicationCommandHandlerTests
{
    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_ShouldSubmitApplication_WhenValidRequestWithAppId(
        SubmitApplicationCommand command,
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

        var handler = CreateHandler(
            applicationRepo,
            AuthenticatedUserServiceTestHelper.MockReturningUser(userWithExternalId),
            permissionCheckerService,
            unitOfWork);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(command.ApplicationId, result.Value.ApplicationId);
        Assert.Equal(ApplicationStatus.Submitted, result.Value.Status);
        Assert.NotNull(result.Value.DateSubmitted);
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_ShouldSubmitApplication_WhenValidRequestWithEmail(
        SubmitApplicationCommand command,
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

        var handler = CreateHandler(
            applicationRepo,
            AuthenticatedUserServiceTestHelper.MockReturningUser(testUser),
            permissionCheckerService,
            unitOfWork);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(ApplicationStatus.Submitted, result.Value.Status);
        Assert.NotNull(result.Value.DateSubmitted);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldReturnUnauthorized_WhenUserNotAuthenticated(
        SubmitApplicationCommand command,
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
        SubmitApplicationCommand command,
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
        SubmitApplicationCommand command,
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
        Assert.Equal("User does not have permission to submit this application", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_ShouldReturnError_WhenApplicationAlreadySubmitted(
        SubmitApplicationCommand command,
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
            ApplicationStatus.Submitted);

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
        Assert.Contains("Application has already been submitted", result.Error!);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization), typeof(UserCustomization))]
    public async Task Handle_ShouldReturnForbidden_WhenUserIsNotApplicationCreator(
        SubmitApplicationCommand command,
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

        var differentUserId = new UserId(Guid.NewGuid());
        var applicationId = new ApplicationId(command.ApplicationId);
        var application = new Domain.Entities.Application(
            applicationId,
            "APP-001",
            new TemplateVersionId(Guid.NewGuid()),
            DateTime.UtcNow,
            differentUserId,
            ApplicationStatus.InProgress);

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
        Assert.Equal("Only the user who created the application can submit it", result.Error);
    }

    private static SubmitApplicationCommandHandler CreateHandler(
        IEaRepository<Domain.Entities.Application> applicationRepo,
        IAuthenticatedUserService authenticatedUserService,
        IPermissionCheckerService permissionCheckerService,
        IUnitOfWork unitOfWork,
        IEaRepository<Domain.Entities.File>? fileRepository = null,
        IFileValidationModeResolver? modeResolver = null,
        IApplicationFileValidationPolicy? policy = null)
    {
        var files = fileRepository ?? Substitute.For<IEaRepository<Domain.Entities.File>>();
        var emptyFiles = Array.Empty<Domain.Entities.File>().AsQueryable().BuildMockDbSet();
        files.Query().Returns(emptyFiles);

        var resolver = modeResolver ?? Substitute.For<IFileValidationModeResolver>();
        resolver.Resolve(Arg.Any<Guid?>()).Returns(FileValidationMode.Off);

        return new SubmitApplicationCommandHandler(
            applicationRepo,
            files,
            authenticatedUserService,
            permissionCheckerService,
            resolver,
            policy ?? new ApplicationFileValidationPolicy(),
            Substitute.For<IUserCacheInvalidator>(),
            unitOfWork);
    }
}
