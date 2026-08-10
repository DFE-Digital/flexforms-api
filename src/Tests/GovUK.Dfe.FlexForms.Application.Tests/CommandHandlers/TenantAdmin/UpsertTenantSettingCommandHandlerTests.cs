using GovUK.Dfe.FlexForms.Application.TenantAdmin.Commands;
using GovUK.Dfe.FlexForms.Application.TenantAdmin.Validation;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using System.Security.Claims;
using System.Text;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.TenantAdmin;

public class UpsertTenantSettingCommandHandlerTests
{
    private readonly ITenantSettingsWriter _writer = Substitute.For<ITenantSettingsWriter>();
    private readonly ITenantContextAccessor _tenantContext = Substitute.For<ITenantContextAccessor>();
    private readonly IPermissionCheckerService _permissionChecker = Substitute.For<IPermissionCheckerService>();
    private readonly ITenantConfigurationProvider _configProvider = Substitute.For<ITenantConfigurationProvider>();
    private readonly ITenantSettingAuditWriter _auditWriter = Substitute.For<ITenantSettingAuditWriter>();
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
    private readonly UpsertTenantSettingCommandHandler _handler;

    public UpsertTenantSettingCommandHandlerTests()
    {
        _handler = new UpsertTenantSettingCommandHandler(
            _writer,
            _tenantContext,
            _permissionChecker,
            _configProvider,
            _auditWriter,
            _httpContextAccessor);
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenCallerIsNotSuperAdmin()
    {
        _permissionChecker.IsInteractivePlatformAdmin().Returns(false);

        var result = await _handler.Handle(
            new UpsertTenantSettingCommand(Guid.NewGuid(), "Layout", "Web", ToBase64("{}"), false),
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

        _permissionChecker.IsInteractivePlatformAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(CreateTenant(callerTenantId, "Transfers"));

        var result = await _handler.Handle(
            new UpsertTenantSettingCommand(otherTenantId, "Layout", "Web", ToBase64("{}"), false),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _writer.DidNotReceiveWithAnyArgs().UpsertSettingAsync(default, default!, default!, default!, default, default);
        await _configProvider.DidNotReceiveWithAnyArgs().RefreshAsync(default);
    }

    [Fact]
    public async Task Handle_ShouldUpsertAndRefresh_WhenSuperAdminUpdatesOwnTenant()
    {
        var tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        _permissionChecker.IsInteractivePlatformAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(CreateTenant(tenantId, "Transfers"));

        _writer.UpsertSettingAsync(
                tenantId, "Layout", "Web", "{}", false, Arg.Any<CancellationToken>())
            .Returns(new UpsertTenantSettingResult(Guid.NewGuid(), true, "Layout", "Web"));

        var result = await _handler.Handle(
            new UpsertTenantSettingCommand(tenantId, "Layout", "Web", ToBase64("{}"), false),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.WasCreated);
        await _writer.Received(1).UpsertSettingAsync(
            tenantId, "Layout", "Web", "{}", false, Arg.Any<CancellationToken>());
        await _auditWriter.Received(1).AppendAsync(
            tenantId, "Layout", "Web", "Created", Arg.Any<string>(), false, Arg.Any<CancellationToken>());
        await _configProvider.Received(1).RefreshAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldForceSecret_WhenCategoryRequiresEncryption()
    {
        var tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        _permissionChecker.IsInteractivePlatformAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(CreateTenant(tenantId, "Transfers"));

        var json = """{"SecretKey":"k","Issuer":"i","Audience":"a"}""";
        _writer.UpsertSettingAsync(
                tenantId, "Authorization", "Api", json, true, Arg.Any<CancellationToken>())
            .Returns(new UpsertTenantSettingResult(Guid.NewGuid(), false, "Authorization", "Api"));

        var result = await _handler.Handle(
            new UpsertTenantSettingCommand(tenantId, "Authorization", "Api", ToBase64(json), false),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        await _writer.Received(1).UpsertSettingAsync(
            tenantId, "Authorization", "Api", json, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldFail_WhenSettingsJsonIsNotBase64()
    {
        var tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        _permissionChecker.IsInteractivePlatformAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(CreateTenant(tenantId, "Transfers"));

        var result = await _handler.Handle(
            new UpsertTenantSettingCommand(tenantId, "Layout", "Web", "{not-base64}", false),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Base64", result.Error);
        await _writer.DidNotReceiveWithAnyArgs().UpsertSettingAsync(default, default!, default!, default!, default, default);
    }

    [Fact]
    public async Task Handle_ShouldValidateCategoryJson()
    {
        var tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        _permissionChecker.IsInteractivePlatformAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(CreateTenant(tenantId, "Transfers"));

        var result = await _handler.Handle(
            new UpsertTenantSettingCommand(
                tenantId,
                "TestAuthentication",
                "Shared",
                ToBase64("""{"Enabled":true}"""),
                false),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Validation, result.ErrorCode);
        await _writer.DidNotReceiveWithAnyArgs().UpsertSettingAsync(default, default!, default!, default!, default, default);
    }

    private static string ToBase64(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

    private static TenantConfiguration CreateTenant(Guid id, string name) =>
        new(id, name, new ConfigurationBuilder().Build(), Array.Empty<string>());
}
