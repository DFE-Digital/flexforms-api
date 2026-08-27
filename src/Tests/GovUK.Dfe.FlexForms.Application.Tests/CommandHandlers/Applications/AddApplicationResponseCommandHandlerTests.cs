using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.FlexForms.Application.Applications.Commands;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Tests.Helpers;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;
using MediatR;
using NSubstitute;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.Applications;

public class AddApplicationResponseCommandHandlerTests
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
    public async Task Handle_ShouldAddResponseVersion_WhenValidRequest(
        Guid applicationId,
        string responseBody,
        IPermissionCheckerService permissionCheckerService,
        IApplicationRepository applicationRepository,
        IApplicationResponseAppender responseAppender)
    {
        var encodedBody = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(responseBody));
        var command = new AddApplicationResponseCommand(applicationId, encodedBody);

        var user = new User(new UserId(Guid.NewGuid()), new RoleId(Guid.NewGuid()), "Test User", "test@example.com", DateTime.UtcNow, null, null, null);
        permissionCheckerService.HasPermission(ResourceType.Application, command.ApplicationId.ToString(), AccessType.Write).Returns(true);

        var appDomainId = new ApplicationId(command.ApplicationId);
        var now = DateTime.UtcNow;
        var newResponse = new ApplicationResponse(new ResponseId(Guid.NewGuid()), appDomainId, responseBody, now, user.Id!);
        var domainEvent = new GovUK.Dfe.FlexForms.Domain.Events.ApplicationResponseAddedEvent(appDomainId, newResponse.Id!, user.Id!, now);
        responseAppender.Create(appDomainId, responseBody, user.Id!, Arg.Any<DateTime?>())
            .Returns(new ApplicationResponseAppendResult(now, newResponse, domainEvent));

        applicationRepository.AppendResponseVersionAsync(appDomainId, newResponse, now, user.Id!, Arg.Any<CancellationToken>())
            .Returns(("APP-001", newResponse));

        var mediator = Substitute.For<IMediator>();
        var handler = new AddApplicationResponseCommandHandler(
            AuthenticatedUserServiceTestHelper.MockReturningUser(user),
            permissionCheckerService,
            applicationRepository,
            responseAppender,
            Substitute.For<IUserCacheInvalidator>(),
            CreateTenantPermissionFilter(),
            mediator);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(command.ApplicationId, result.Value.ApplicationId);
        Assert.Equal(responseBody, result.Value.ResponseBody);
        Assert.Equal(user.Id!.Value, result.Value.CreatedBy);
        await mediator.Received(1).Publish(domainEvent, Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldAddResponseVersion_WhenValidRequestWithExternalId(
        Guid applicationId,
        string responseBody,
        IPermissionCheckerService permissionCheckerService,
        IApplicationRepository applicationRepository,
        IApplicationResponseAppender responseAppender)
    {
        var encodedBody = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(responseBody));
        var command = new AddApplicationResponseCommand(applicationId, encodedBody);

        var user = new User(new UserId(Guid.NewGuid()), new RoleId(Guid.NewGuid()), "Test User", "test@example.com", DateTime.UtcNow, null, null, null, "external-id");
        permissionCheckerService.HasPermission(ResourceType.Application, command.ApplicationId.ToString(), AccessType.Write).Returns(true);

        var appDomainId = new ApplicationId(command.ApplicationId);
        var now = DateTime.UtcNow;
        var newResponse = new ApplicationResponse(new ResponseId(Guid.NewGuid()), appDomainId, responseBody, now, user.Id!);
        var domainEvent = new GovUK.Dfe.FlexForms.Domain.Events.ApplicationResponseAddedEvent(appDomainId, newResponse.Id!, user.Id!, now);
        responseAppender.Create(appDomainId, responseBody, user.Id!, Arg.Any<DateTime?>())
            .Returns(new ApplicationResponseAppendResult(now, newResponse, domainEvent));

        applicationRepository.AppendResponseVersionAsync(appDomainId, newResponse, now, user.Id!, Arg.Any<CancellationToken>())
            .Returns(("APP-001", newResponse));

        var mediator = Substitute.For<IMediator>();
        var handler = new AddApplicationResponseCommandHandler(
            AuthenticatedUserServiceTestHelper.MockReturningUser(user),
            permissionCheckerService,
            applicationRepository,
            responseAppender,
            Substitute.For<IUserCacheInvalidator>(),
            CreateTenantPermissionFilter(),
            mediator);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(command.ApplicationId, result.Value.ApplicationId);
        Assert.Equal(responseBody, result.Value.ResponseBody);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldReturnFailure_WhenUserNotAuthenticated(
        AddApplicationResponseCommand command,
        IPermissionCheckerService permissionCheckerService,
        IApplicationRepository applicationRepository,
        IApplicationResponseAppender responseAppender)
    {
        var mediator = Substitute.For<IMediator>();
        var handler = new AddApplicationResponseCommandHandler(
            AuthenticatedUserServiceTestHelper.MockReturning(Result<User>.Forbid("Not authenticated")),
            permissionCheckerService,
            applicationRepository,
            responseAppender,
            Substitute.For<IUserCacheInvalidator>(),
            CreateTenantPermissionFilter(),
            mediator);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Not authenticated", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldReturnFailure_WhenUserNotFound(
        AddApplicationResponseCommand command,
        IPermissionCheckerService permissionCheckerService,
        IApplicationRepository applicationRepository,
        IApplicationResponseAppender responseAppender)
    {
        var mediator = Substitute.For<IMediator>();
        var handler = new AddApplicationResponseCommandHandler(
            AuthenticatedUserServiceTestHelper.MockReturning(Result<User>.NotFound("User not found")),
            permissionCheckerService,
            applicationRepository,
            responseAppender,
            Substitute.For<IUserCacheInvalidator>(),
            CreateTenantPermissionFilter(),
            mediator);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("User not found", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldReturnFailure_WhenUserLacksPermission(
        AddApplicationResponseCommand command,
        IPermissionCheckerService permissionCheckerService,
        IApplicationRepository applicationRepository,
        IApplicationResponseAppender responseAppender)
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

        permissionCheckerService.HasPermission(ResourceType.Application, command.ApplicationId.ToString(), AccessType.Write).Returns(false);

        var mediator = Substitute.For<IMediator>();
        var handler = new AddApplicationResponseCommandHandler(
            AuthenticatedUserServiceTestHelper.MockReturningUser(user),
            permissionCheckerService,
            applicationRepository,
            responseAppender,
            Substitute.For<IUserCacheInvalidator>(),
            CreateTenantPermissionFilter(),
            mediator);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("User does not have permission to update this application", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldReturnFailure_WhenApplicationNotFound(
        AddApplicationResponseCommand command,
        IPermissionCheckerService permissionCheckerService,
        IApplicationRepository applicationRepository,
        IApplicationResponseAppender responseAppender)
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

        permissionCheckerService.HasPermission(ResourceType.Application, command.ApplicationId.ToString(), AccessType.Write).Returns(true);

        command = command with
        {
            ResponseBody = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("some body"))
        };

        responseAppender.Create(Arg.Any<ApplicationId>(), Arg.Any<string>(), Arg.Any<UserId>(), Arg.Any<DateTime?>())
            .Returns(ci =>
            {
                var appId = (ApplicationId)ci[0]!;
                var body = (string)ci[1]!;
                var createdBy = (UserId)ci[2]!;
                var now = DateTime.UtcNow;
                var response = new ApplicationResponse(new ResponseId(Guid.NewGuid()), appId, body, now, createdBy);
                var evt = new GovUK.Dfe.FlexForms.Domain.Events.ApplicationResponseAddedEvent(appId, response.Id!, createdBy, now);
                return new ApplicationResponseAppendResult(now, response, evt);
            });

        applicationRepository.AppendResponseVersionAsync(
                Arg.Any<ApplicationId>(),
                Arg.Any<ApplicationResponse>(),
                Arg.Any<DateTime>(),
                Arg.Any<UserId>(),
                Arg.Any<CancellationToken>())
            .Returns((ValueTuple<string, ApplicationResponse>?)null);

        var mediator = Substitute.For<IMediator>();
        var handler = new AddApplicationResponseCommandHandler(
            AuthenticatedUserServiceTestHelper.MockReturningUser(user),
            permissionCheckerService,
            applicationRepository,
            responseAppender,
            Substitute.For<IUserCacheInvalidator>(),
            CreateTenantPermissionFilter(),
            mediator);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Application not found", result.Error);
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldReturnFailure_WhenBodyIsInvalidBase64(
        Guid applicationId,
        IPermissionCheckerService permissionCheckerService,
        IApplicationRepository applicationRepository,
        IApplicationResponseAppender responseAppender)
    {
        var command = new AddApplicationResponseCommand(applicationId, "this is not base64");
        var user = new User(new UserId(Guid.NewGuid()), new RoleId(Guid.NewGuid()), "Test User", "test@example.com", DateTime.UtcNow, null, null, null);
        permissionCheckerService.HasPermission(ResourceType.Application, command.ApplicationId.ToString(), AccessType.Write).Returns(true);

        var mediator = Substitute.For<IMediator>();
        var handler = new AddApplicationResponseCommandHandler(
            AuthenticatedUserServiceTestHelper.MockReturningUser(user),
            permissionCheckerService,
            applicationRepository,
            responseAppender,
            Substitute.For<IUserCacheInvalidator>(),
            CreateTenantPermissionFilter(),
            mediator);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Invalid Base64 format for ResponseBody", result.Error);
    }
}
