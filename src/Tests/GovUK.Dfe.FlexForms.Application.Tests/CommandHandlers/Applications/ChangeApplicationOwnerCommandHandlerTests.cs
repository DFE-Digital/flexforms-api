using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Applications.Commands;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Tests.Helpers;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using MockQueryable.NSubstitute;
using NSubstitute;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.Applications;

public class ChangeApplicationOwnerCommandHandlerTests
{
    private readonly UserFactory _userFactory = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task Handle_MovesOwnershipAndApplicationPermissions()
    {
        var templateId = new TemplateId(Guid.NewGuid());
        var actor = UserNamed("Admin", "admin@education.gov.uk");
        var previous = UserNamed("Old Owner", "old.owner@education.gov.uk");
        var next = UserNamed("New Owner", "new.owner@education.gov.uk");
        var application = ApplicationOwnedBy(previous, templateId, ApplicationStatus.InProgress);

        _userFactory.AddPermissionToUser(
            previous,
            application.Id!.Value.ToString(),
            ResourceType.Application,
            [AccessType.Read, AccessType.Write],
            actor.Id!,
            application.Id);
        _userFactory.AddPermissionToUser(
            previous,
            application.Id.Value.ToString(),
            ResourceType.ApplicationFiles,
            [AccessType.Read, AccessType.Write, AccessType.Delete],
            actor.Id!,
            application.Id);
        _userFactory.AddTemplatePermissionToUser(
            previous,
            templateId.Value.ToString(),
            [AccessType.Write],
            actor.Id!);
        _userFactory.AddTemplatePermissionToUser(
            next,
            templateId.Value.ToString(),
            [AccessType.Write],
            actor.Id!);

        var handler = CreateHandler(application, [previous, next], actor, member: true);
        var result = await handler.Handle(
            new ChangeApplicationOwnerCommand(application.Id.Value, next.Email),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.TemplateReadGranted);
        Assert.Equal(next.Id, application.CreatedBy);
        Assert.Contains(next.Permissions, p =>
            p.ResourceType == ResourceType.Application
            && p.AccessType == AccessType.Write
            && p.ResourceKey == application.Id.Value.ToString());
        Assert.Contains(next.Permissions, p =>
            p.ResourceType == ResourceType.ApplicationFiles
            && p.AccessType == AccessType.Delete);
        Assert.DoesNotContain(previous.Permissions, p => p.ApplicationId == application.Id);
        Assert.Contains(previous.Permissions, p =>
            p.ResourceType == ResourceType.Template && p.AccessType == AccessType.Write);
        Assert.Contains(next.Permissions, p =>
            p.ResourceType == ResourceType.Template && p.AccessType == AccessType.Write);
    }

    [Fact]
    public async Task Handle_GrantsTemplateRead_WhenNewOwnerHasNoTemplateAccess()
    {
        var templateId = new TemplateId(Guid.NewGuid());
        var actor = UserNamed("Admin", "admin@education.gov.uk");
        var previous = UserNamed("Old Owner", "old.owner@education.gov.uk");
        var next = UserNamed("New Owner", "new.owner@education.gov.uk");
        var application = ApplicationOwnedBy(previous, templateId, ApplicationStatus.InProgress);

        var handler = CreateHandler(application, [previous, next], actor, member: true);
        var result = await handler.Handle(
            new ChangeApplicationOwnerCommand(application.Id!.Value, next.Email),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.TemplateReadGranted);
        Assert.Contains(next.Permissions, p =>
            p.ResourceType == ResourceType.Template
            && p.AccessType == AccessType.Read
            && p.ResourceKey == templateId.Value.ToString());
        Assert.DoesNotContain(next.Permissions, p =>
            p.ResourceType == ResourceType.Template && p.AccessType == AccessType.Write);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenUserIsNotInTheTenant()
    {
        var templateId = new TemplateId(Guid.NewGuid());
        var actor = UserNamed("Admin", "admin@education.gov.uk");
        var previous = UserNamed("Old Owner", "old.owner@education.gov.uk");
        var next = UserNamed("Outsider", "outsider@education.gov.uk");
        var application = ApplicationOwnedBy(previous, templateId, ApplicationStatus.InProgress);

        var handler = CreateHandler(application, [previous, next], actor, member: false);
        var result = await handler.Handle(
            new ChangeApplicationOwnerCommand(application.Id!.Value, next.Email),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
        Assert.Equal("User not found", result.Error);
        Assert.Equal(previous.Id, application.CreatedBy);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenEmailDoesNotMatchAUser()
    {
        var templateId = new TemplateId(Guid.NewGuid());
        var actor = UserNamed("Admin", "admin@education.gov.uk");
        var previous = UserNamed("Old Owner", "old.owner@education.gov.uk");
        var application = ApplicationOwnedBy(previous, templateId, ApplicationStatus.InProgress);

        var handler = CreateHandler(application, [previous], actor, member: true);
        var result = await handler.Handle(
            new ChangeApplicationOwnerCommand(application.Id!.Value, "missing@education.gov.uk"),
            CancellationToken.None);

        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
        Assert.Equal("User not found", result.Error);
    }

    [Fact]
    public async Task Handle_ReturnsValidation_WhenUserIsAlreadyTheLeadApplicant()
    {
        var templateId = new TemplateId(Guid.NewGuid());
        var actor = UserNamed("Admin", "admin@education.gov.uk");
        var owner = UserNamed("Owner", "owner@education.gov.uk");
        var application = ApplicationOwnedBy(owner, templateId, ApplicationStatus.InProgress);

        var handler = CreateHandler(application, [owner], actor, member: true);
        var result = await handler.Handle(
            new ChangeApplicationOwnerCommand(application.Id!.Value, owner.Email),
            CancellationToken.None);

        Assert.Equal(DomainErrorCode.Validation, result.ErrorCode);
        Assert.Equal("This user is already the lead applicant", result.Error);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenApplicationIsDeleted()
    {
        var templateId = new TemplateId(Guid.NewGuid());
        var actor = UserNamed("Admin", "admin@education.gov.uk");
        var previous = UserNamed("Old Owner", "old.owner@education.gov.uk");
        var next = UserNamed("New Owner", "new.owner@education.gov.uk");
        var application = ApplicationOwnedBy(previous, templateId, ApplicationStatus.Deleted);

        var handler = CreateHandler(application, [previous, next], actor, member: true);
        var result = await handler.Handle(
            new ChangeApplicationOwnerCommand(application.Id!.Value, next.Email),
            CancellationToken.None);

        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
        Assert.Equal("Application not found", result.Error);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenApplicationIsOutsideTheTenant()
    {
        var templateId = new TemplateId(Guid.NewGuid());
        var actor = UserNamed("Admin", "admin@education.gov.uk");
        var previous = UserNamed("Old Owner", "old.owner@education.gov.uk");
        var next = UserNamed("New Owner", "new.owner@education.gov.uk");
        var application = ApplicationOwnedBy(previous, templateId, ApplicationStatus.InProgress);

        var handler = CreateHandler(application, [previous, next], actor, member: true, templateInTenant: false);
        var result = await handler.Handle(
            new ChangeApplicationOwnerCommand(application.Id!.Value, next.Email),
            CancellationToken.None);

        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
        Assert.Equal("Application not found", result.Error);
    }

    private ChangeApplicationOwnerCommandHandler CreateHandler(
        Domain.Entities.Application application,
        IReadOnlyList<User> users,
        User actor,
        bool member,
        bool templateInTenant = true)
    {
        var applications = new[] { application }.AsQueryable().BuildMockDbSet();
        var applicationRepo = Substitute.For<IEaRepository<Domain.Entities.Application>>();
        applicationRepo.Query().Returns(applications);

        var userSet = users.AsQueryable().BuildMockDbSet();
        var userRepo = Substitute.For<IEaRepository<User>>();
        userRepo.Query().Returns(userSet);

        var permissionChecker = Substitute.For<IPermissionCheckerService>();
        permissionChecker.IsAdmin().Returns(true);

        var tenantTemplates = Substitute.For<ITenantTemplateResolver>();
        tenantTemplates.IsTemplateInCurrentTenantAsync(Arg.Any<TemplateId>(), Arg.Any<CancellationToken>())
            .Returns(templateInTenant);

        var tenantContext = Substitute.For<ITenantContextAccessor>();
        tenantContext.CurrentTenant.Returns(new TenantConfiguration(
            _tenantId,
            "test",
            new ConfigurationBuilder().Build(),
            []));

        TenantMembership? activeMembership = member
            ? TenantMembership.Create(_tenantId, users[^1].Id!, new RoleId(Guid.NewGuid()), DateTime.UtcNow)
            : null;
        var membership = Substitute.For<ITenantMembershipService>();
        membership.GetActiveMembershipAsync(Arg.Any<Guid>(), Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns(activeMembership);

        return new ChangeApplicationOwnerCommandHandler(
            applicationRepo,
            userRepo,
            AuthenticatedUserServiceTestHelper.MockReturningUser(actor),
            permissionChecker,
            tenantTemplates,
            tenantContext,
            membership,
            _userFactory,
            Substitute.For<IUserCacheInvalidator>(),
            Substitute.For<IProjectionEventPublisher>(),
            Substitute.For<ITenantAccessAuditWriter>(),
            Substitute.For<IUnitOfWork>());
    }

    private static User UserNamed(string name, string email) =>
        new(
            new UserId(Guid.NewGuid()),
            new RoleId(RoleConstants.UserRoleId),
            name,
            email,
            DateTime.UtcNow,
            null,
            null,
            null);

    private static Domain.Entities.Application ApplicationOwnedBy(
        User owner,
        TemplateId templateId,
        ApplicationStatus status)
    {
        var applicationId = new ApplicationId(Guid.NewGuid());
        var templateVersionId = new TemplateVersionId(Guid.NewGuid());
        var application = new Domain.Entities.Application(
            applicationId,
            "APP-100",
            templateVersionId,
            DateTime.UtcNow,
            owner.Id!,
            status);

        var templateVersion = new TemplateVersion(
            templateVersionId,
            templateId,
            "1.0.0",
            "{}",
            DateTime.UtcNow,
            owner.Id!);
        application.GetType().GetProperty("TemplateVersion")?.SetValue(application, templateVersion);
        return application;
    }
}
