using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Users.Queries;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using MockQueryable;
using NSubstitute;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryHandlers.Users;

public class GetUserCreatedApplicationsLookupQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ShouldReturnCreatedApplicationsAndInvitees()
    {
        var permissionCheckerService = Substitute.For<IPermissionCheckerService>();
        var userRepository = Substitute.For<IEaRepository<User>>();
        var applicationRepository = Substitute.For<IEaRepository<Domain.Entities.Application>>();
        var permissionRepository = Substitute.For<IEaRepository<Permission>>();
        permissionCheckerService.IsAdmin().Returns(true);

        var creator = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(Guid.NewGuid()),
            "Owner",
            "owner@example.test",
            DateTime.UtcNow,
            null, null, null);

        var invitee = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(Guid.NewGuid()),
            "Invited",
            "invitee@example.test",
            DateTime.UtcNow,
            creator.Id, null, null);

        var templateId = new TemplateId(Guid.NewGuid());
        var application = CreateApplication(creator.Id!, "REF-100", templateId);

        var permission = new Permission(
            new PermissionId(Guid.NewGuid()),
            invitee.Id!,
            application.Id,
            application.Id!.Value.ToString(),
            ResourceType.Application,
            AccessType.Write,
            DateTime.UtcNow,
            creator.Id!);
        typeof(Permission).GetProperty(nameof(Permission.User))!.SetValue(permission, invitee);

        var users = new[] { creator }.AsQueryable().BuildMock();
        var apps = new[] { application }.AsQueryable().BuildMock();
        var permissions = new[] { permission }.AsQueryable().BuildMock();
        userRepository.Query().Returns(users);
        applicationRepository.Query().Returns(apps);
        permissionRepository.Query().Returns(permissions);

        var handler = CreateHandler(
            permissionCheckerService,
            userRepository,
            applicationRepository,
            permissionRepository,
            membership: TenantMembership.Create(TenantId, creator.Id!, creator.RoleId, DateTime.UtcNow),
            catalogueTemplateIds: [templateId]);

        var result = await handler.Handle(
            new GetUserCreatedApplicationsLookupQuery("owner@example.test"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(creator.Id!.Value, result.Value!.UserId);
        Assert.Equal("owner@example.test", result.Value.Email);
        var created = Assert.Single(result.Value.Applications);
        Assert.Equal("REF-100", created.ApplicationReference);
        var invited = Assert.Single(created.Invitees);
        Assert.Equal(invitee.Id!.Value, invited.UserId);
        Assert.Equal("invitee@example.test", invited.Email);
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenCallerIsNotAdmin()
    {
        var permissionCheckerService = Substitute.For<IPermissionCheckerService>();
        permissionCheckerService.IsAdmin().Returns(false);
        permissionCheckerService.CanManageUsers().Returns(true);

        var handler = CreateHandler(
            permissionCheckerService,
            Substitute.For<IEaRepository<User>>(),
            Substitute.For<IEaRepository<Domain.Entities.Application>>(),
            Substitute.For<IEaRepository<Permission>>());

        var result = await handler.Handle(
            new GetUserCreatedApplicationsLookupQuery("anyone@example.test"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFound_WhenUserDoesNotExist()
    {
        var permissionCheckerService = Substitute.For<IPermissionCheckerService>();
        var userRepository = Substitute.For<IEaRepository<User>>();
        permissionCheckerService.IsAdmin().Returns(true);
        var users = Array.Empty<User>().AsQueryable().BuildMock();
        userRepository.Query().Returns(users);

        var handler = CreateHandler(
            permissionCheckerService,
            userRepository,
            Substitute.For<IEaRepository<Domain.Entities.Application>>(),
            Substitute.For<IEaRepository<Permission>>());

        var result = await handler.Handle(
            new GetUserCreatedApplicationsLookupQuery("missing@example.test"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFound_WhenUserIsNotAMemberOfCurrentTenant()
    {
        var permissionCheckerService = Substitute.For<IPermissionCheckerService>();
        var userRepository = Substitute.For<IEaRepository<User>>();
        permissionCheckerService.IsAdmin().Returns(true);

        var otherTenantUser = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(Guid.NewGuid()),
            "Other tenant",
            "other@example.test",
            DateTime.UtcNow,
            null, null, null);

        userRepository.Query().Returns(new[] { otherTenantUser }.AsQueryable().BuildMock());

        var handler = CreateHandler(
            permissionCheckerService,
            userRepository,
            Substitute.For<IEaRepository<Domain.Entities.Application>>(),
            Substitute.For<IEaRepository<Permission>>(),
            membership: null);

        var result = await handler.Handle(
            new GetUserCreatedApplicationsLookupQuery("other@example.test"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
        Assert.Equal("User not found", result.Error);
    }

    [Fact]
    public async Task Handle_ShouldExcludeApplicationsFromTemplatesOutsideCurrentTenant()
    {
        var permissionCheckerService = Substitute.For<IPermissionCheckerService>();
        var userRepository = Substitute.For<IEaRepository<User>>();
        var applicationRepository = Substitute.For<IEaRepository<Domain.Entities.Application>>();
        var permissionRepository = Substitute.For<IEaRepository<Permission>>();
        permissionCheckerService.IsAdmin().Returns(true);

        var creator = new User(
            new UserId(Guid.NewGuid()),
            new RoleId(Guid.NewGuid()),
            "Owner",
            "owner@example.test",
            DateTime.UtcNow,
            null, null, null);

        var tenantTemplateId = new TemplateId(Guid.NewGuid());
        var otherTemplateId = new TemplateId(Guid.NewGuid());
        var tenantApp = CreateApplication(creator.Id!, "REF-TENANT", tenantTemplateId);
        var otherApp = CreateApplication(creator.Id!, "REF-OTHER", otherTemplateId);

        userRepository.Query().Returns(new[] { creator }.AsQueryable().BuildMock());
        applicationRepository.Query().Returns(new[] { tenantApp, otherApp }.AsQueryable().BuildMock());
        permissionRepository.Query().Returns(Array.Empty<Permission>().AsQueryable().BuildMock());

        var handler = CreateHandler(
            permissionCheckerService,
            userRepository,
            applicationRepository,
            permissionRepository,
            membership: TenantMembership.Create(TenantId, creator.Id!, creator.RoleId, DateTime.UtcNow),
            catalogueTemplateIds: [tenantTemplateId]);

        var result = await handler.Handle(
            new GetUserCreatedApplicationsLookupQuery("owner@example.test"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var created = Assert.Single(result.Value!.Applications);
        Assert.Equal("REF-TENANT", created.ApplicationReference);
    }

    private static GetUserCreatedApplicationsLookupQueryHandler CreateHandler(
        IPermissionCheckerService permissionCheckerService,
        IEaRepository<User> userRepository,
        IEaRepository<Domain.Entities.Application> applicationRepository,
        IEaRepository<Permission> permissionRepository,
        TenantMembership? membership = null,
        IReadOnlyList<TemplateId>? catalogueTemplateIds = null)
    {
        var tenantContextAccessor = Substitute.For<ITenantContextAccessor>();
        tenantContextAccessor.CurrentTenant.Returns(new TenantConfiguration(
            TenantId,
            "TestTenant",
            new ConfigurationBuilder().Build(),
            []));

        var tenantMembershipService = Substitute.For<ITenantMembershipService>();
        tenantMembershipService.GetActiveMembershipAsync(
                TenantId,
                Arg.Any<UserId>(),
                Arg.Any<CancellationToken>())
            .Returns(membership);

        var tenantTemplateCatalogue = Substitute.For<ITenantTemplateCatalogue>();
        tenantTemplateCatalogue.GetTemplateIdsAsync(Arg.Any<CancellationToken>())
            .Returns(catalogueTemplateIds ?? []);

        return new GetUserCreatedApplicationsLookupQueryHandler(
            permissionCheckerService,
            tenantContextAccessor,
            tenantMembershipService,
            tenantTemplateCatalogue,
            userRepository,
            applicationRepository,
            permissionRepository);
    }

    private static Domain.Entities.Application CreateApplication(
        UserId createdBy,
        string reference,
        TemplateId templateId)
    {
        var versionId = new TemplateVersionId(Guid.NewGuid());
        var application = new Domain.Entities.Application(
            new ApplicationId(Guid.NewGuid()),
            reference,
            versionId,
            DateTime.UtcNow,
            createdBy);

        var version = new TemplateVersion(
            versionId,
            templateId,
            "1.0",
            "{}",
            DateTime.UtcNow,
            createdBy);

        typeof(Domain.Entities.Application)
            .GetProperty(nameof(Domain.Entities.Application.TemplateVersion))!
            .SetValue(application, version);

        return application;
    }
}
