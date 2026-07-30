using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.TenantAdmin;
using GovUK.Dfe.FlexForms.Application.TenantAdmin.Queries;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryHandlers.TenantAdmin;

public class GetTenantSettingsQueryHandlerTests
{
    private readonly ITenantSettingsQuery _settingsQuery = Substitute.For<ITenantSettingsQuery>();
    private readonly ITenantContextAccessor _tenantContext = Substitute.For<ITenantContextAccessor>();
    private readonly IPermissionCheckerService _permissionChecker = Substitute.For<IPermissionCheckerService>();
    private readonly GetTenantSettingsQueryHandler _handler;

    public GetTenantSettingsQueryHandlerTests()
    {
        _handler = new GetTenantSettingsQueryHandler(_settingsQuery, _tenantContext, _permissionChecker);
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenCallerIsNotPlatformAdmin()
    {
        _permissionChecker.IsInteractivePlatformAdmin().Returns(false);

        var result = await _handler.Handle(
            new GetTenantSettingsQuery(Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _settingsQuery.DidNotReceiveWithAnyArgs().ListSettingsAsync(default, default);
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenRouteTenantDoesNotMatchCurrentTenant()
    {
        var callerTenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var otherTenantId = Guid.Parse("22222222-2222-4222-8222-222222222222");

        _permissionChecker.IsInteractivePlatformAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(CreateTenant(callerTenantId, "Transfers"));

        var result = await _handler.Handle(
            new GetTenantSettingsQuery(otherTenantId),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _settingsQuery.DidNotReceiveWithAnyArgs().ListSettingsAsync(default, default);
    }

    [Fact]
    public async Task Handle_ShouldReturnSettings_WhenSuperAdminViewsOwnTenant()
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
                        "Layout",
                        "Web",
                        """{"ServiceName":"Test"}""",
                        false,
                        DateTime.UtcNow)
                ]));

        var result = await _handler.Handle(
            new GetTenantSettingsQuery(tenantId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(tenantId, result.Value!.TenantId);
        Assert.Equal("Transfers", result.Value.TenantName);
        Assert.Single(result.Value.Settings);
        Assert.Equal("Layout", result.Value.Settings.First().Category);
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFound_WhenTenantMissing()
    {
        var tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        _permissionChecker.IsInteractivePlatformAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(CreateTenant(tenantId, "Transfers"));
        _settingsQuery.ListSettingsAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns((TenantSettingsList?)null);

        var result = await _handler.Handle(
            new GetTenantSettingsQuery(tenantId),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
    }

    private static TenantConfiguration CreateTenant(Guid id, string name) =>
        new(id, name, new ConfigurationBuilder().Build(), Array.Empty<string>());
}
