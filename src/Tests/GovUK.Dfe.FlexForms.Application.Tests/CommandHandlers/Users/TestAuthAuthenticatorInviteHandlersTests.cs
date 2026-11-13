using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Application.Users.Commands;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.NSubstitute;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.Users;

public class TestAuthAuthenticatorInviteHandlersTests
{
    private const string AdminEmail = "admin@education.gov.uk";
    private const string TesterEmail = "tester@education.gov.uk";
    private static readonly DateTimeOffset ExpiresAt = new(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
    private readonly ITenantContextAccessor _tenantContextAccessor = Substitute.For<ITenantContextAccessor>();
    private readonly IHostEnvironment _hostEnvironment = Substitute.For<IHostEnvironment>();
    private readonly IPermissionCheckerService _permissionChecker = Substitute.For<IPermissionCheckerService>();
    private readonly IEaRepository<User> _userRepository = Substitute.For<IEaRepository<User>>();
    private readonly ITenantMembershipService _membershipService = Substitute.For<ITenantMembershipService>();
    private readonly ITestAuthAuthenticatorInviteService _inviteService = Substitute.For<ITestAuthAuthenticatorInviteService>();
    private readonly ITestAuthAuthenticatorService _authenticatorService = Substitute.For<ITestAuthAuthenticatorService>();
    private readonly ITenantAccessAuditWriter _auditWriter = Substitute.For<ITenantAccessAuditWriter>();
    private readonly List<User> _users = [];

    public TestAuthAuthenticatorInviteHandlersTests()
    {
        _hostEnvironment.EnvironmentName.Returns("Development");
        SetTenant(testAuthEnabled: true);
        _permissionChecker.CanManageUsers().Returns(true);
        _httpContextAccessor.HttpContext.Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, AdminEmail)], "TenantBearer"))
        });
        _userRepository.Query().Returns(_ => _users.AsQueryable().BuildMockDbSet());
        _inviteService.IssueAsync(Arg.Any<string>()).Returns(new TestAuthAuthenticatorInvite("ABCDE-FGHJK", ExpiresAt));
    }

    [Fact]
    public async Task Create_issues_code_for_someone_who_has_never_signed_in_and_audits_it()
    {
        var admin = AddUser(AdminEmail, RoleConstants.UserRoleId);

        var result = await CreateHandler().Handle(
            new CreateTestAuthAuthenticatorInviteCommand($" {TesterEmail} "),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("ABCDE-FGHJK", result.Value!.Code);
        Assert.Equal(ExpiresAt.UtcDateTime, result.Value.ExpiresAtUtc);
        await _inviteService.Received(1).IssueAsync(TesterEmail);
        await _auditWriter.Received(1).AppendAsync(
            _tenantId,
            Arg.Is<UserId?>(id => id == null),
            TesterEmail,
            CreateTestAuthAuthenticatorInviteCommandHandler.AuditAction,
            Arg.Is<string?>(role => role == null),
            Arg.Is<UserId?>(id => id == admin.Id),
            AdminEmail,
            Arg.Is<string?>(d => d != null && d.Contains("expires")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_is_forbidden_when_test_auth_disabled()
    {
        SetTenant(testAuthEnabled: false);

        var result = await CreateHandler().Handle(new CreateTestAuthAuthenticatorInviteCommand(TesterEmail), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _inviteService.DidNotReceiveWithAnyArgs().IssueAsync(default!);
    }

    [Fact]
    public async Task Create_is_forbidden_in_production()
    {
        _hostEnvironment.EnvironmentName.Returns(Environments.Production);

        var result = await CreateHandler().Handle(new CreateTestAuthAuthenticatorInviteCommand(TesterEmail), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
    }

    [Fact]
    public async Task Create_is_forbidden_for_callers_who_cannot_manage_users()
    {
        _permissionChecker.CanManageUsers().Returns(false);

        var result = await CreateHandler().Handle(new CreateTestAuthAuthenticatorInviteCommand(TesterEmail), CancellationToken.None);

        Assert.Equal(CreateTestAuthAuthenticatorInviteCommandHandler.NotAllowedMessage, result.Error);
        await _inviteService.DidNotReceiveWithAnyArgs().IssueAsync(default!);
    }

    [Fact]
    public async Task Create_by_tenant_admin_is_forbidden_for_another_tenant_admin()
    {
        var subject = AddUser(TesterEmail, RoleConstants.UserRoleId);
        GiveMembership(subject, RoleNames.Admin);

        var result = await CreateHandler().Handle(new CreateTestAuthAuthenticatorInviteCommand(TesterEmail), CancellationToken.None);

        Assert.Equal(CreateTestAuthAuthenticatorInviteCommandHandler.AdministratorTargetMessage, result.Error);
        await _inviteService.DidNotReceiveWithAnyArgs().IssueAsync(default!);
    }

    [Fact]
    public async Task Create_by_tenant_admin_is_forbidden_for_a_platform_superadmin()
    {
        AddUser(TesterEmail, RoleConstants.AdminRoleId);

        var result = await CreateHandler().Handle(new CreateTestAuthAuthenticatorInviteCommand(TesterEmail), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
    }

    [Fact]
    public async Task Create_by_tenant_admin_is_allowed_for_an_ordinary_user()
    {
        var subject = AddUser(TesterEmail, RoleConstants.UserRoleId);
        GiveMembership(subject, RoleNames.User);

        var result = await CreateHandler().Handle(new CreateTestAuthAuthenticatorInviteCommand(TesterEmail), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task Create_by_tenant_admin_is_allowed_for_themselves()
    {
        var admin = AddUser(AdminEmail, RoleConstants.UserRoleId);
        GiveMembership(admin, RoleNames.Admin);

        var result = await CreateHandler().Handle(new CreateTestAuthAuthenticatorInviteCommand(AdminEmail), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task Create_by_superadmin_is_allowed_for_an_administrator()
    {
        _permissionChecker.IsInteractivePlatformAdmin().Returns(true);
        var subject = AddUser(TesterEmail, RoleConstants.UserRoleId);
        GiveMembership(subject, RoleNames.Admin);

        var result = await CreateHandler().Handle(new CreateTestAuthAuthenticatorInviteCommand(TesterEmail), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task Redeem_starts_setup_from_the_code_with_service_name_issuer()
    {
        _inviteService.RedeemAsync(TesterEmail, "abcde fghjk").Returns(true);
        _authenticatorService.BeginInviteSetupAsync(TesterEmail, "Transfers (Development)", Arg.Any<CancellationToken>())
            .Returns(new TestAuthAuthenticatorSetup("KEY", "otpauth://totp/x"));

        var result = await RedeemHandler().Handle(
            new RedeemTestAuthAuthenticatorInviteCommand($" {TesterEmail} ", "abcde fghjk"),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("KEY", result.Value!.SharedKey);
        Assert.Equal("otpauth://totp/x", result.Value.AuthenticatorUri);
    }

    [Fact]
    public async Task Redeem_fails_without_starting_setup_when_code_is_invalid()
    {
        _inviteService.RedeemAsync(TesterEmail, "WRONG").Returns(false);

        var result = await RedeemHandler().Handle(new RedeemTestAuthAuthenticatorInviteCommand(TesterEmail, "WRONG"), CancellationToken.None);

        Assert.Equal(DomainErrorCode.BadRequest, result.ErrorCode);
        Assert.Equal(RedeemTestAuthAuthenticatorInviteCommandHandler.InvalidCodeMessage, result.Error);
        await _authenticatorService.DidNotReceiveWithAnyArgs().BeginInviteSetupAsync(default!, default!, default);
    }

    [Fact]
    public async Task Redeem_is_forbidden_when_test_auth_disabled()
    {
        SetTenant(testAuthEnabled: false);

        var result = await RedeemHandler().Handle(new RedeemTestAuthAuthenticatorInviteCommand(TesterEmail, "X"), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _inviteService.DidNotReceiveWithAnyArgs().RedeemAsync(default!, default!);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Confirm_returns_whether_the_invite_setup_was_completed(bool enrolled)
    {
        _authenticatorService.ConfirmInviteSetupAsync(TesterEmail, "123456", Arg.Any<CancellationToken>()).Returns(enrolled);

        var result = await ConfirmHandler().Handle(
            new ConfirmTestAuthAuthenticatorInviteSetupCommand($" {TesterEmail} ", "123456"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(enrolled, result.Value!.IsValid);
        await _authenticatorService.DidNotReceiveWithAnyArgs().ConfirmSetupAsync(default!, default!, default);
    }

    [Fact]
    public async Task Confirm_is_forbidden_in_production()
    {
        _hostEnvironment.EnvironmentName.Returns(Environments.Production);

        var result = await ConfirmHandler().Handle(
            new ConfirmTestAuthAuthenticatorInviteSetupCommand(TesterEmail, "123456"),
            CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
    }

    private CreateTestAuthAuthenticatorInviteCommandHandler CreateHandler()
        => new(
            _httpContextAccessor,
            _tenantContextAccessor,
            _hostEnvironment,
            _permissionChecker,
            _userRepository,
            _membershipService,
            _inviteService,
            _auditWriter);

    private RedeemTestAuthAuthenticatorInviteCommandHandler RedeemHandler()
        => new(
            NullLogger<RedeemTestAuthAuthenticatorInviteCommandHandler>.Instance,
            _tenantContextAccessor,
            _hostEnvironment,
            _inviteService,
            _authenticatorService);

    private ConfirmTestAuthAuthenticatorInviteSetupCommandHandler ConfirmHandler()
        => new(
            NullLogger<ConfirmTestAuthAuthenticatorInviteSetupCommandHandler>.Instance,
            _tenantContextAccessor,
            _hostEnvironment,
            _authenticatorService);

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

    private void SetTenant(bool testAuthEnabled)
    {
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TestAuthentication:Enabled"] = testAuthEnabled.ToString(),
                ["Layout:ServiceName"] = "Transfers"
            })
            .Build();

        _tenantContextAccessor.CurrentTenant.Returns(new TenantConfiguration(_tenantId, "TestTenant", settings, []));
    }
}
