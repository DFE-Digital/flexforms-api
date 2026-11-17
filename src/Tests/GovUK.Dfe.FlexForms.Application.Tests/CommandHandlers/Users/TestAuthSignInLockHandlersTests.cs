using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Application.Users;
using GovUK.Dfe.FlexForms.Application.Users.Commands;
using GovUK.Dfe.FlexForms.Application.Users.Queries;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MockQueryable.NSubstitute;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.Users;

public class TestAuthSignInLockHandlersTests
{
    private const string AdminEmail = "admin@education.gov.uk";
    private const string TesterEmail = "tester@education.gov.uk";
    private static readonly DateTime LockedAt = new(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
    private readonly ITenantContextAccessor _tenantContextAccessor = Substitute.For<ITenantContextAccessor>();
    private readonly IHostEnvironment _hostEnvironment = Substitute.For<IHostEnvironment>();
    private readonly IPermissionCheckerService _permissionChecker = Substitute.For<IPermissionCheckerService>();
    private readonly IEaRepository<User> _userRepository = Substitute.For<IEaRepository<User>>();
    private readonly ITenantMembershipService _membershipService = Substitute.For<ITenantMembershipService>();
    private readonly ITestAuthSignInLockService _lockService = Substitute.For<ITestAuthSignInLockService>();
    private readonly ITenantAccessAuditWriter _auditWriter = Substitute.For<ITenantAccessAuditWriter>();
    private readonly List<User> _users = [];
    private readonly User _admin;

    public TestAuthSignInLockHandlersTests()
    {
        _hostEnvironment.EnvironmentName.Returns("Development");
        SetTenant(testAuthEnabled: true);
        _permissionChecker.CanManageUsers().Returns(true);
        SetActor(new Claim(ClaimTypes.Email, AdminEmail));
        _userRepository.Query().Returns(_ => _users.AsQueryable().BuildMockDbSet());
        _lockService.GetStatusAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TestAuthSignInLockStatus(false, null, null, null));

        _admin = AddUser(AdminEmail, RoleConstants.UserRoleId);
        GiveMembership(_admin, RoleNames.Admin);
    }

    [Fact]
    public async Task Get_returns_manual_and_temporary_lock_status()
    {
        var subject = AddMember(TesterEmail, RoleNames.User);
        _lockService.GetStatusAsync(TesterEmail, Arg.Any<CancellationToken>())
            .Returns(new TestAuthSignInLockStatus(true, LockedAt, AdminEmail, TimeSpan.FromSeconds(119.5)));

        var result = await GetHandler().Handle(new GetTestAuthSignInLockQuery(subject.Id!.Value), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Value!.IsManuallyLocked);
        Assert.Equal(LockedAt, result.Value.LockedAtUtc);
        Assert.Equal(AdminEmail, result.Value.LockedByEmail);
        Assert.True(result.Value.IsTemporarilyLocked);
        Assert.Equal(120, result.Value.RetryAfterSeconds);
    }

    [Fact]
    public async Task Get_is_not_found_for_a_user_outside_the_tenant()
    {
        var outsider = AddUser(TesterEmail, RoleConstants.UserRoleId);

        var result = await GetHandler().Handle(new GetTestAuthSignInLockQuery(outsider.Id!.Value), CancellationToken.None);

        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task Get_is_forbidden_when_test_auth_is_disabled()
    {
        SetTenant(testAuthEnabled: false);
        var subject = AddMember(TesterEmail, RoleNames.User);

        var result = await GetHandler().Handle(new GetTestAuthSignInLockQuery(subject.Id!.Value), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
    }

    [Fact]
    public async Task Get_is_forbidden_for_service_principals()
    {
        SetActor(new Claim("appid", "service-principal"));
        var subject = AddMember(TesterEmail, RoleNames.User);

        var result = await GetHandler().Handle(new GetTestAuthSignInLockQuery(subject.Id!.Value), CancellationToken.None);

        Assert.Equal(TestAuthSignInLockAdminGuard.NotAllowedMessage, result.Error);
    }

    [Fact]
    public async Task Lock_locks_the_user_and_audits_it()
    {
        var subject = AddMember(TesterEmail, RoleNames.User);
        _lockService.LockAsync(TesterEmail, AdminEmail, Arg.Any<CancellationToken>()).Returns(true);

        var result = await SetHandler().Handle(new SetTestAuthSignInLockCommand(subject.Id!.Value, true), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        await _lockService.Received(1).LockAsync(TesterEmail, AdminEmail, Arg.Any<CancellationToken>());
        await _auditWriter.Received(1).AppendAsync(
            _tenantId,
            subject.Id,
            TesterEmail,
            SetTestAuthSignInLockCommandHandler.LockedAuditAction,
            Arg.Is<string?>(role => role == null),
            _admin.Id,
            AdminEmail,
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lock_does_not_audit_when_already_locked()
    {
        var subject = AddMember(TesterEmail, RoleNames.User);
        _lockService.LockAsync(TesterEmail, AdminEmail, Arg.Any<CancellationToken>()).Returns(false);

        var result = await SetHandler().Handle(new SetTestAuthSignInLockCommand(subject.Id!.Value, true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await _auditWriter.DidNotReceiveWithAnyArgs().AppendAsync(default, default, default!, default!, default, default, default!, default, default);
    }

    [Fact]
    public async Task Unlock_unlocks_the_user_and_audits_it()
    {
        var subject = AddMember(TesterEmail, RoleNames.User);
        _lockService.UnlockAsync(TesterEmail, Arg.Any<CancellationToken>()).Returns(true);

        var result = await SetHandler().Handle(new SetTestAuthSignInLockCommand(subject.Id!.Value, false), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        await _auditWriter.Received(1).AppendAsync(
            _tenantId,
            subject.Id,
            TesterEmail,
            SetTestAuthSignInLockCommandHandler.UnlockedAuditAction,
            Arg.Is<string?>(role => role == null),
            _admin.Id,
            AdminEmail,
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lock_refuses_to_lock_yourself_out()
    {
        var result = await SetHandler().Handle(new SetTestAuthSignInLockCommand(_admin.Id!.Value, true), CancellationToken.None);

        Assert.Equal(TestAuthSignInLockAdminGuard.SelfLockMessage, result.Error);
        await _lockService.DidNotReceiveWithAnyArgs().LockAsync(default!, default!, default);
    }

    [Fact]
    public async Task Tenant_admin_cannot_lock_another_administrator()
    {
        var subject = AddMember(TesterEmail, RoleNames.Admin);

        var result = await SetHandler().Handle(new SetTestAuthSignInLockCommand(subject.Id!.Value, true), CancellationToken.None);

        Assert.Equal(TestAuthSignInLockAdminGuard.AdministratorTargetMessage, result.Error);
        await _lockService.DidNotReceiveWithAnyArgs().LockAsync(default!, default!, default);
    }

    [Fact]
    public async Task SuperAdmin_can_lock_another_administrator()
    {
        _permissionChecker.IsInteractivePlatformAdmin().Returns(true);
        var subject = AddMember(TesterEmail, RoleNames.Admin);

        var result = await SetHandler().Handle(new SetTestAuthSignInLockCommand(subject.Id!.Value, true), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        await _lockService.Received(1).LockAsync(TesterEmail, AdminEmail, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClearTemporary_clears_and_audits_only_when_temporarily_locked()
    {
        var subject = AddMember(TesterEmail, RoleNames.User);
        _lockService.GetStatusAsync(TesterEmail, Arg.Any<CancellationToken>())
            .Returns(
                new TestAuthSignInLockStatus(false, null, null, TimeSpan.FromMinutes(3)),
                new TestAuthSignInLockStatus(false, null, null, null));

        var result = await ClearHandler().Handle(new ClearTestAuthTemporaryLockoutCommand(subject.Id!.Value), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Value!.IsTemporarilyLocked);
        await _lockService.Received(1).ClearTemporaryLockoutAsync(TesterEmail, Arg.Any<CancellationToken>());
        await _auditWriter.Received(1).AppendAsync(
            _tenantId,
            subject.Id,
            TesterEmail,
            ClearTestAuthTemporaryLockoutCommandHandler.AuditAction,
            Arg.Is<string?>(role => role == null),
            _admin.Id,
            AdminEmail,
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClearTemporary_does_nothing_when_not_temporarily_locked()
    {
        var subject = AddMember(TesterEmail, RoleNames.User);

        var result = await ClearHandler().Handle(new ClearTestAuthTemporaryLockoutCommand(subject.Id!.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await _lockService.DidNotReceiveWithAnyArgs().ClearTemporaryLockoutAsync(default!, default);
        await _auditWriter.DidNotReceiveWithAnyArgs().AppendAsync(default, default, default!, default!, default, default, default!, default, default);
    }

    private TestAuthSignInLockAdminGuard Guard() => new(
        _httpContextAccessor,
        _tenantContextAccessor,
        _hostEnvironment,
        _permissionChecker,
        _userRepository,
        _membershipService);

    private GetTestAuthSignInLockQueryHandler GetHandler() => new(Guard(), _lockService);

    private SetTestAuthSignInLockCommandHandler SetHandler() => new(Guard(), _lockService, _auditWriter);

    private ClearTestAuthTemporaryLockoutCommandHandler ClearHandler() => new(Guard(), _lockService, _auditWriter);

    private User AddMember(string email, string roleName)
    {
        var user = AddUser(email, RoleConstants.UserRoleId);
        GiveMembership(user, roleName);
        return user;
    }

    private User AddUser(string email, Guid roleId)
    {
        var user = new User(new UserId(Guid.NewGuid()), new RoleId(roleId), email, email, DateTime.UtcNow, null, null, null);
        _users.Add(user);
        return user;
    }

    private void GiveMembership(User user, string roleName)
    {
        var role = Role.CreateForTenant(_tenantId, roleName, true);
        var membership = new TenantMembership(
            new TenantMembershipId(Guid.NewGuid()),
            _tenantId,
            user.Id!,
            role.Id!,
            DateTime.UtcNow);
        membership.GetType().GetProperty(nameof(TenantMembership.Role))!.SetValue(membership, role);
        _membershipService.GetActiveMembershipAsync(_tenantId, user.Id!, Arg.Any<CancellationToken>()).Returns(membership);
    }

    private void SetActor(params Claim[] claims)
        => _httpContextAccessor.HttpContext.Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TenantBearer"))
        });

    private void SetTenant(bool testAuthEnabled)
    {
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TestAuthentication:Enabled"] = testAuthEnabled.ToString()
            })
            .Build();

        _tenantContextAccessor.CurrentTenant.Returns(new TenantConfiguration(_tenantId, "TestTenant", settings, []));
    }
}
