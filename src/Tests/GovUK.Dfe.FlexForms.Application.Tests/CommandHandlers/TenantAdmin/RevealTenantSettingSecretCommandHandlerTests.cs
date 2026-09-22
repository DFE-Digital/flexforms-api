using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Utilities.RateLimiting;
using GovUK.Dfe.FlexForms.Application.Common.Exceptions;
using GovUK.Dfe.FlexForms.Application.TenantAdmin.Commands;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.TenantAdmin;

public class RevealTenantSettingSecretCommandHandlerTests
{
    private readonly ITenantSettingsQuery _settingsQuery = Substitute.For<ITenantSettingsQuery>();
    private readonly ITenantContextAccessor _tenantContext = Substitute.For<ITenantContextAccessor>();
    private readonly IPermissionCheckerService _permissionChecker = Substitute.For<IPermissionCheckerService>();
    private readonly ITenantSettingAuditWriter _auditWriter = Substitute.For<ITenantSettingAuditWriter>();
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
    private readonly IRateLimiterFactory<string> _rateLimiterFactory = Substitute.For<IRateLimiterFactory<string>>();
    private readonly IRateLimiter<string> _rateLimiter = Substitute.For<IRateLimiter<string>>();
    private readonly RevealTenantSettingSecretCommandHandler _handler;

    public RevealTenantSettingSecretCommandHandlerTests()
    {
        _rateLimiter.IsAllowed(Arg.Any<string>()).Returns(true);
        _rateLimiterFactory.Create(Arg.Any<int>(), Arg.Any<TimeSpan>()).Returns(_rateLimiter);
        _httpContextAccessor.HttpContext.Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Email, "super@education.gov.uk")],
                "Test"))
        });
        _handler = new RevealTenantSettingSecretCommandHandler(
            _settingsQuery,
            _tenantContext,
            _permissionChecker,
            _auditWriter,
            _httpContextAccessor,
            _rateLimiterFactory);
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenCallerIsNotSuperAdmin()
    {
        _permissionChecker.IsInteractivePlatformAdmin().Returns(false);

        var result = await _handler.Handle(
            Command(Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _settingsQuery.DidNotReceiveWithAnyArgs().ListSettingsAsync(default, default);
    }

    [Fact]
    public async Task Handle_ShouldReturnValue_AndAudit_WhenSuperAdminRevealsOwnTenantSecret()
    {
        var tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        _permissionChecker.IsInteractivePlatformAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(CreateTenant(tenantId, "Transfers"));
        _settingsQuery.ListSettingsAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new TenantSettingsList(
                tenantId,
                "Transfers",
                [
                    new TenantSettingRow(
                        Guid.NewGuid(),
                        "Authorization",
                        "Api",
                        """{"SecretKey":"super-secret","Issuer":"i"}""",
                        true,
                        DateTime.UtcNow)
                ]));

        var result = await _handler.Handle(
            new RevealTenantSettingSecretCommand(
                tenantId,
                "Authorization",
                "Api",
                "SecretKey",
                "Investigating expired tokens"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("super-secret", result.Value!.Value);
        Assert.Equal("SecretKey", result.Value.Path);
        await _auditWriter.Received(1).AppendAsync(
            tenantId,
            "Authorization",
            "Api",
            TenantSettingSecretRedaction.RevealAuditAction,
            "super@education.gov.uk",
            true,
            Arg.Any<CancellationToken>(),
            Arg.Is<string>(d => d.Contains("SecretKey") && d.Contains("Investigating")));
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenRateLimitExceeded()
    {
        var tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        _permissionChecker.IsInteractivePlatformAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(CreateTenant(tenantId, "Transfers"));
        _rateLimiter.IsAllowed(Arg.Any<string>()).Returns(false);

        await Assert.ThrowsAsync<RateLimitExceededException>(() =>
            _handler.Handle(
                new RevealTenantSettingSecretCommand(
                    tenantId,
                    "Authorization",
                    "Api",
                    "SecretKey",
                    "Investigating expired tokens"),
                CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFound_WhenPathMissing()
    {
        var tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        _permissionChecker.IsInteractivePlatformAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(CreateTenant(tenantId, "Transfers"));
        _settingsQuery.ListSettingsAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new TenantSettingsList(
                tenantId,
                "Transfers",
                [
                    new TenantSettingRow(
                        Guid.NewGuid(),
                        "Authorization",
                        "Api",
                        """{"SecretKey":"super-secret"}""",
                        true,
                        DateTime.UtcNow)
                ]));

        var result = await _handler.Handle(
            new RevealTenantSettingSecretCommand(
                tenantId,
                "Authorization",
                "Api",
                "Missing",
                "Investigating expired tokens"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
        await _auditWriter.DidNotReceiveWithAnyArgs().AppendAsync(
            default, default!, default!, default!, default!, default, default, default);
    }

    private static RevealTenantSettingSecretCommand Command(Guid tenantId) =>
        new(tenantId, "Authorization", "Api", "SecretKey", "Investigating expired tokens");

    private static TenantConfiguration CreateTenant(Guid id, string name) =>
        new(id, name, new ConfigurationBuilder().Build(), Array.Empty<string>());
}
