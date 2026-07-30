using GovUK.Dfe.FlexForms.Application.TenantAdmin.Commands;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.TenantAdmin;

public class UpsertTenantSettingCommandHandlerTests
{
    private readonly ITenantSettingsWriter _writer = Substitute.For<ITenantSettingsWriter>();
    private readonly ITenantContextAccessor _tenantContext = Substitute.For<ITenantContextAccessor>();
    private readonly IPermissionCheckerService _permissionChecker = Substitute.For<IPermissionCheckerService>();
    private readonly ITenantConfigurationProvider _configProvider = Substitute.For<ITenantConfigurationProvider>();
    private readonly UpsertTenantSettingCommandHandler _handler;

    public UpsertTenantSettingCommandHandlerTests()
    {
        _handler = new UpsertTenantSettingCommandHandler(
            _writer,
            _tenantContext,
            _permissionChecker,
            _configProvider);
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenCallerIsNotAdmin()
    {
        _permissionChecker.IsInteractiveTenantAdmin().Returns(false);

        var result = await _handler.Handle(
            new UpsertTenantSettingCommand(Guid.NewGuid(), "Layout", "Web", "{}", false),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _writer.DidNotReceiveWithAnyArgs().UpsertSettingAsync(default, default!, default!, default!, default, default);
        await _configProvider.DidNotReceiveWithAnyArgs().RefreshAsync(default);
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenRouteTenantDoesNotMatchCurrentTenant()
    {
        var callerTenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var otherTenantId = Guid.Parse("22222222-2222-4222-8222-222222222222");

        _permissionChecker.IsInteractiveTenantAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(CreateTenant(callerTenantId, "Transfers"));

        var result = await _handler.Handle(
            new UpsertTenantSettingCommand(otherTenantId, "Layout", "Web", "{}", false),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _writer.DidNotReceiveWithAnyArgs().UpsertSettingAsync(default, default!, default!, default!, default, default);
        await _configProvider.DidNotReceiveWithAnyArgs().RefreshAsync(default);
    }

    [Fact]
    public async Task Handle_ShouldUpsertAndRefresh_WhenAdminUpdatesOwnTenant()
    {
        var tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        _permissionChecker.IsInteractiveTenantAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(CreateTenant(tenantId, "Transfers"));

        _writer.UpsertSettingAsync(
                tenantId, "Layout", "Web", "{}", false, Arg.Any<CancellationToken>())
            .Returns(new UpsertTenantSettingResult(Guid.NewGuid(), true, "Layout", "Web"));

        var result = await _handler.Handle(
            new UpsertTenantSettingCommand(tenantId, "Layout", "Web", "{}", false),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.WasCreated);
        await _writer.Received(1).UpsertSettingAsync(
            tenantId, "Layout", "Web", "{}", false, Arg.Any<CancellationToken>());
        await _configProvider.Received(1).RefreshAsync(Arg.Any<CancellationToken>());
    }

    private static TenantConfiguration CreateTenant(Guid id, string name) =>
        new(id, name, new ConfigurationBuilder().Build(), Array.Empty<string>());
}
