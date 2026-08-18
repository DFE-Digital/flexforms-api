using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Templates.Commands;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using MockQueryable.NSubstitute;
using NSubstitute;
using System.Security.Claims;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.Templates;

public class GrantTemplateAccessToAllUsersCommandHandlerTests
{
    private readonly IEaRepository<TenantMembership> _membershipRepo = Substitute.For<IEaRepository<TenantMembership>>();
    private readonly IEaRepository<User> _userRepo = Substitute.For<IEaRepository<User>>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IUserFactory _userFactory = Substitute.For<IUserFactory>();
    private readonly ITenantTemplateCatalogue _catalogue = Substitute.For<ITenantTemplateCatalogue>();
    private readonly ITenantContextAccessor _tenantContextAccessor = Substitute.For<ITenantContextAccessor>();
    private readonly IPermissionCheckerService _permissionChecker = Substitute.For<IPermissionCheckerService>();
    private readonly IUserCacheInvalidator _cacheInvalidator = Substitute.For<IUserCacheInvalidator>();
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
    private readonly GrantTemplateAccessToAllUsersCommandHandler _handler;

    public GrantTemplateAccessToAllUsersCommandHandlerTests()
    {
        _handler = new GrantTemplateAccessToAllUsersCommandHandler(
            _membershipRepo,
            _userRepo,
            _unitOfWork,
            _userFactory,
            _catalogue,
            _tenantContextAccessor,
            _permissionChecker,
            _cacheInvalidator,
            _httpContextAccessor);
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenCallerCannotManageTemplates()
    {
        _permissionChecker.CanManageTemplates().Returns(false);

        var result = await _handler.Handle(
            new GrantTemplateAccessToAllUsersCommand(Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("template administrators", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFound_WhenTemplateNotInTenantCatalogue()
    {
        var tenantId = Guid.NewGuid();
        var admin = CreateUser("Admin", "admin@education.gov.uk", Role.CreateForTenant(tenantId, RoleNames.Admin, true).Id!);
        SetupCaller(tenantId, admin);
        _catalogue.ContainsAsync(Arg.Any<TemplateId>(), Arg.Any<CancellationToken>()).Returns(false);

        var users = new[] { admin }.AsQueryable().BuildMockDbSet();
        _userRepo.Query().Returns(users);

        var result = await _handler.Handle(
            new GrantTemplateAccessToAllUsersCommand(Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_ShouldGrantMissingUsers_AndSkipThoseWithAccess()
    {
        var tenantId = Guid.NewGuid();
        var templateId = new TemplateId(Guid.NewGuid());
        var role = Role.CreateForTenant(tenantId, RoleNames.User, true);
        var admin = CreateUser("Admin", "admin@education.gov.uk", role.Id!);
        var needsGrant = CreateUser("Needs", "needs@example.com", role.Id!);
        var alreadyHas = CreateUser("Has", "has@example.com", role.Id!, templateId);

        SetupCaller(tenantId, admin, templateId);
        _catalogue.ContainsAsync(templateId, Arg.Any<CancellationToken>()).Returns(true);

        var memberships = new[]
        {
            CreateMembership(tenantId, needsGrant, role),
            CreateMembership(tenantId, alreadyHas, role),
            CreateMembership(tenantId, admin, role)
        }.AsQueryable().BuildMockDbSet();
        _membershipRepo.Query().Returns(memberships);

        var users = new[] { needsGrant, alreadyHas, admin }.AsQueryable().BuildMockDbSet();
        _userRepo.Query().Returns(users);

        var result = await _handler.Handle(
            new GrantTemplateAccessToAllUsersCommand(templateId.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(3, result.Value!.TotalUsers);
        Assert.Equal(2, result.Value.UsersGranted);
        Assert.Equal(1, result.Value.UsersAlreadyHadAccess);

        _userFactory.Received(1).EnsureUserHasTemplatePermission(
            needsGrant,
            templateId,
            admin.Id!,
            Arg.Any<DateTime?>());
        _userFactory.Received(1).EnsureUserHasTemplatePermission(
            admin,
            templateId,
            admin.Id!,
            Arg.Any<DateTime?>());
        _userFactory.DidNotReceive().EnsureUserHasTemplatePermission(
            alreadyHas,
            Arg.Any<TemplateId>(),
            Arg.Any<UserId>(),
            Arg.Any<DateTime?>());

        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await _cacheInvalidator.Received(1).InvalidateTenantUserClaimsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldNotCommit_WhenEveryoneAlreadyHasAccess()
    {
        var tenantId = Guid.NewGuid();
        var templateId = new TemplateId(Guid.NewGuid());
        var role = Role.CreateForTenant(tenantId, RoleNames.User, true);
        var admin = CreateUser("Admin", "admin@education.gov.uk", role.Id!);
        var user = CreateUser("User", "user@example.com", role.Id!, templateId);

        SetupCaller(tenantId, admin, templateId);
        _catalogue.ContainsAsync(templateId, Arg.Any<CancellationToken>()).Returns(true);

        var memberships = new[] { CreateMembership(tenantId, user, role) }.AsQueryable().BuildMockDbSet();
        _membershipRepo.Query().Returns(memberships);

        var users = new[] { user, admin }.AsQueryable().BuildMockDbSet();
        _userRepo.Query().Returns(users);

        var result = await _handler.Handle(
            new GrantTemplateAccessToAllUsersCommand(templateId.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0, result.Value!.UsersGranted);
        Assert.Equal(1, result.Value.UsersAlreadyHadAccess);
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await _cacheInvalidator.DidNotReceive().InvalidateTenantUserClaimsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldSkipContributorOnlyUsers()
    {
        var tenantId = Guid.NewGuid();
        var templateId = new TemplateId(Guid.NewGuid());
        var invitedTemplateId = new TemplateId(Guid.NewGuid());
        var role = Role.CreateForTenant(tenantId, RoleNames.User, true);
        var admin = CreateUser("Admin", "admin@education.gov.uk", role.Id!);
        var contributor = CreateContributorOnlyUser("Contributor", "contributor@example.com", role.Id!, invitedTemplateId);

        SetupCaller(tenantId, admin, templateId, invitedTemplateId);
        _catalogue.ContainsAsync(templateId, Arg.Any<CancellationToken>()).Returns(true);

        var memberships = new[] { CreateMembership(tenantId, contributor, role) }.AsQueryable().BuildMockDbSet();
        _membershipRepo.Query().Returns(memberships);

        var users = new[] { contributor, admin }.AsQueryable().BuildMockDbSet();
        _userRepo.Query().Returns(users);

        var result = await _handler.Handle(
            new GrantTemplateAccessToAllUsersCommand(templateId.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, result.Value!.TotalUsers);
        Assert.Equal(0, result.Value.UsersGranted);
        Assert.Equal(0, result.Value.UsersAlreadyHadAccess);
        _userFactory.DidNotReceive().EnsureUserHasTemplatePermission(
            contributor,
            Arg.Any<TemplateId>(),
            Arg.Any<UserId>(),
            Arg.Any<DateTime?>());
        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldGrantWrite_WhenExistingMemberWasInvitedAndOnlyHasReadOnThatTemplate()
    {
        var tenantId = Guid.NewGuid();
        var templateToGrant = new TemplateId(Guid.NewGuid());
        var existingMemberTemplate = new TemplateId(Guid.NewGuid());
        var role = Role.CreateForTenant(tenantId, RoleNames.User, true);
        var admin = CreateUser("Admin", "admin@education.gov.uk", role.Id!);
        var member = CreateExistingMemberInvitedAsContributor(
            "Member",
            "member@example.com",
            role.Id!,
            existingMemberTemplate,
            templateToGrant);

        SetupCaller(tenantId, admin, templateToGrant, existingMemberTemplate);
        _catalogue.ContainsAsync(templateToGrant, Arg.Any<CancellationToken>()).Returns(true);

        var memberships = new[] { CreateMembership(tenantId, member, role) }.AsQueryable().BuildMockDbSet();
        _membershipRepo.Query().Returns(memberships);

        var users = new[] { member, admin }.AsQueryable().BuildMockDbSet();
        _userRepo.Query().Returns(users);

        var result = await _handler.Handle(
            new GrantTemplateAccessToAllUsersCommand(templateToGrant.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, result.Value!.UsersGranted);
        _userFactory.Received(1).EnsureUserHasTemplatePermission(
            member,
            templateToGrant,
            admin.Id!,
            Arg.Any<DateTime?>());
    }

    [Fact]
    public async Task Handle_ShouldSkipContributor_WhenTemplateWriteIsOnlyOnAnotherTenant()
    {
        var tenantId = Guid.NewGuid();
        var templateId = new TemplateId(Guid.NewGuid());
        var invitedTemplateId = new TemplateId(Guid.NewGuid());
        var otherTenantTemplateId = new TemplateId(Guid.NewGuid());
        var role = Role.CreateForTenant(tenantId, RoleNames.User, true);
        var admin = CreateUser("Admin", "admin@education.gov.uk", role.Id!);
        var contributor = CreateContributorOnlyUser(
            "Contributor",
            "contributor@example.com",
            role.Id!,
            invitedTemplateId);
        new UserFactory().AddTemplatePermissionToUser(
            contributor,
            otherTenantTemplateId.Value.ToString(),
            [AccessType.Read, AccessType.Write],
            admin.Id!);

        SetupCaller(tenantId, admin, templateId, invitedTemplateId);
        _catalogue.ContainsAsync(templateId, Arg.Any<CancellationToken>()).Returns(true);

        var memberships = new[] { CreateMembership(tenantId, contributor, role) }.AsQueryable().BuildMockDbSet();
        _membershipRepo.Query().Returns(memberships);

        var users = new[] { contributor, admin }.AsQueryable().BuildMockDbSet();
        _userRepo.Query().Returns(users);

        var result = await _handler.Handle(
            new GrantTemplateAccessToAllUsersCommand(templateId.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0, result.Value!.UsersGranted);
        _userFactory.DidNotReceive().EnsureUserHasTemplatePermission(
            contributor,
            Arg.Any<TemplateId>(),
            Arg.Any<UserId>(),
            Arg.Any<DateTime?>());
    }

    private void SetupCaller(Guid tenantId, User admin, params TemplateId[] catalogueTemplateIds)
    {
        _permissionChecker.CanManageTemplates().Returns(true);
        _tenantContextAccessor.CurrentTenant.Returns(new TenantConfiguration(
            tenantId,
            "Transfers",
            new ConfigurationBuilder().Build(),
            []));

        var httpContext = Substitute.For<HttpContext>();
        httpContext.User.Returns(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, admin.Email)],
            authenticationType: "Bearer")));
        _httpContextAccessor.HttpContext.Returns(httpContext);

        _catalogue.GetTemplateIdsAsync(Arg.Any<CancellationToken>())
            .Returns(catalogueTemplateIds.ToList().AsReadOnly());
    }

    private static User CreateUser(
        string name,
        string email,
        RoleId roleId,
        TemplateId? withTemplateAccess = null)
    {
        var userId = new UserId(Guid.NewGuid());
        IEnumerable<Permission>? perms = null;
        if (withTemplateAccess is not null)
        {
            perms =
            [
                new Permission(
                    new PermissionId(Guid.NewGuid()),
                    userId,
                    applicationId: null,
                    withTemplateAccess.Value.ToString(),
                    ResourceType.Template,
                    AccessType.Read,
                    DateTime.UtcNow,
                    userId),
                new Permission(
                    new PermissionId(Guid.NewGuid()),
                    userId,
                    applicationId: null,
                    withTemplateAccess.Value.ToString(),
                    ResourceType.Template,
                    AccessType.Write,
                    DateTime.UtcNow,
                    userId)
            ];
        }

        return new User(
            userId,
            roleId,
            name,
            email,
            DateTime.UtcNow,
            null,
            null,
            null,
            initialPermissions: perms);
    }

    private static User CreateContributorOnlyUser(
        string name,
        string email,
        RoleId roleId,
        TemplateId invitedTemplateId)
    {
        var userId = new UserId(Guid.NewGuid());
        var applicationId = new ApplicationId(Guid.NewGuid());
        var perms = new[]
        {
            new Permission(
                new PermissionId(Guid.NewGuid()),
                userId,
                applicationId,
                applicationId.Value.ToString(),
                ResourceType.Application,
                AccessType.Write,
                DateTime.UtcNow,
                userId),
            new Permission(
                new PermissionId(Guid.NewGuid()),
                userId,
                applicationId: null,
                invitedTemplateId.Value.ToString(),
                ResourceType.Template,
                AccessType.Read,
                DateTime.UtcNow,
                userId)
        };

        return new User(
            userId,
            roleId,
            name,
            email,
            DateTime.UtcNow,
            null,
            null,
            null,
            initialPermissions: perms);
    }

    private static User CreateExistingMemberInvitedAsContributor(
        string name,
        string email,
        RoleId roleId,
        TemplateId existingWriteTemplateId,
        TemplateId invitedTemplateId)
    {
        var userId = new UserId(Guid.NewGuid());
        var applicationId = new ApplicationId(Guid.NewGuid());
        var perms = new[]
        {
            new Permission(
                new PermissionId(Guid.NewGuid()),
                userId,
                applicationId: null,
                existingWriteTemplateId.Value.ToString(),
                ResourceType.Template,
                AccessType.Read,
                DateTime.UtcNow,
                userId),
            new Permission(
                new PermissionId(Guid.NewGuid()),
                userId,
                applicationId: null,
                existingWriteTemplateId.Value.ToString(),
                ResourceType.Template,
                AccessType.Write,
                DateTime.UtcNow,
                userId),
            new Permission(
                new PermissionId(Guid.NewGuid()),
                userId,
                applicationId: null,
                invitedTemplateId.Value.ToString(),
                ResourceType.Template,
                AccessType.Read,
                DateTime.UtcNow,
                userId),
            new Permission(
                new PermissionId(Guid.NewGuid()),
                userId,
                applicationId,
                applicationId.Value.ToString(),
                ResourceType.Application,
                AccessType.Write,
                DateTime.UtcNow,
                userId)
        };

        return new User(
            userId,
            roleId,
            name,
            email,
            DateTime.UtcNow,
            null,
            null,
            null,
            initialPermissions: perms);
    }

    private static TenantMembership CreateMembership(Guid tenantId, User user, Role role)
    {
        var membership = new TenantMembership(
            new TenantMembershipId(Guid.NewGuid()),
            tenantId,
            user.Id!,
            role.Id!,
            DateTime.UtcNow,
            isActive: true);
        membership.GetType().GetProperty(nameof(TenantMembership.User))!.SetValue(membership, user);
        membership.GetType().GetProperty(nameof(TenantMembership.Role))!.SetValue(membership, role);
        return membership;
    }
}
