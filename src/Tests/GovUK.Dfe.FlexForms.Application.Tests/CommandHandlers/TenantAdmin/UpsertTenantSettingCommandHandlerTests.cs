using GovUK.Dfe.FlexForms.Application.TenantAdmin.Commands;
using GovUK.Dfe.FlexForms.Application.TenantAdmin.Validation;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
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
    private readonly ITemplateHostMappingOwnershipValidator _ownershipValidator =
        Substitute.For<ITemplateHostMappingOwnershipValidator>();
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
    private readonly IHostEnvironment _hostEnvironment = Substitute.For<IHostEnvironment>();
    private readonly UpsertTenantSettingCommandHandler _handler;

    public UpsertTenantSettingCommandHandlerTests()
    {
        _hostEnvironment.EnvironmentName.Returns("Development");
        _ownershipValidator
            .ValidateAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());
        _handler = new UpsertTenantSettingCommandHandler(
            _writer,
            _tenantContext,
            _permissionChecker,
            _configProvider,
            _auditWriter,
            _ownershipValidator,
            _httpContextAccessor,
            _hostEnvironment);
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenCallerIsNotSuperAdmin()
    {
        _permissionChecker.IsInteractiveTenantAdmin().Returns(false);

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

        _permissionChecker.IsInteractiveTenantAdmin().Returns(true);
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
        _permissionChecker.IsInteractiveTenantAdmin().Returns(true);
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
        _permissionChecker.IsInteractiveTenantAdmin().Returns(true);
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
    public async Task Handle_ShouldForbid_WhenTenantAdminUpdatesApplicationTemplates()
    {
        var tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        _permissionChecker.IsInteractiveTenantAdmin().Returns(true);
        _permissionChecker.IsInteractivePlatformAdmin().Returns(false);
        _tenantContext.CurrentTenant.Returns(CreateTenant(tenantId, "Transfers"));

        var result = await _handler.Handle(
            new UpsertTenantSettingCommand(
                tenantId,
                "ApplicationTemplates",
                "Api",
                ToBase64("""{"HostMappings":{}}"""),
                false),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _writer.DidNotReceiveWithAnyArgs().UpsertSettingAsync(default, default!, default!, default!, default, default);
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenTenantAdminUpdatesConnectionStrings()
    {
        var tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        _permissionChecker.IsInteractiveTenantAdmin().Returns(true);
        _permissionChecker.IsInteractivePlatformAdmin().Returns(false);
        _tenantContext.CurrentTenant.Returns(CreateTenant(tenantId, "Transfers"));

        var result = await _handler.Handle(
            new UpsertTenantSettingCommand(
                tenantId,
                "ConnectionStrings",
                "Api",
                ToBase64("""{"DefaultConnection":"Server=.;Database=x;"}"""),
                true),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _writer.DidNotReceiveWithAnyArgs().UpsertSettingAsync(default, default!, default!, default!, default, default);
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenTenantAdminUpdatesApplicationInsights()
    {
        var tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        _permissionChecker.IsInteractiveTenantAdmin().Returns(true);
        _permissionChecker.IsInteractivePlatformAdmin().Returns(false);
        _tenantContext.CurrentTenant.Returns(CreateTenant(tenantId, "Transfers"));

        var result = await _handler.Handle(
            new UpsertTenantSettingCommand(
                tenantId,
                "ApplicationInsights",
                "Shared",
                ToBase64("""{"ConnectionString":"InstrumentationKey=test"}"""),
                true),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _writer.DidNotReceiveWithAnyArgs().UpsertSettingAsync(default, default!, default!, default!, default, default);
    }

    [Fact]
    public async Task Handle_ShouldFail_WhenSettingsJsonIsNotBase64()
    {
        var tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        _permissionChecker.IsInteractiveTenantAdmin().Returns(true);
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
        _permissionChecker.IsInteractiveTenantAdmin().Returns(true);
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

    [Fact]
    public async Task Handle_ShouldRejectEnablingTestAuthentication_InProduction()
    {
        var tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        _permissionChecker.IsInteractiveTenantAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(CreateTenant(tenantId, "Transfers"));
        _hostEnvironment.EnvironmentName.Returns("Production");

        var result = await _handler.Handle(
            new UpsertTenantSettingCommand(
                tenantId,
                "TestAuthentication",
                "Web",
                ToBase64("""{"Enabled":true,"JwtSigningKey":"abcdefghijklmnopqrstuvwxyz012345","JwtIssuer":"iss","JwtAudience":"aud"}"""),
                true),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Validation, result.ErrorCode);
        Assert.Contains("Production", result.Error);
        await _writer.DidNotReceiveWithAnyArgs().UpsertSettingAsync(default, default!, default!, default!, default, default);
    }

    [Fact]
    public async Task Handle_ShouldRejectTestAuthenticationScheme_InProduction()
    {
        var tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        _permissionChecker.IsInteractiveTenantAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(CreateTenant(tenantId, "Transfers"));
        _hostEnvironment.EnvironmentName.Returns("Prod");

        var result = await _handler.Handle(
            new UpsertTenantSettingCommand(
                tenantId,
                "Authentication",
                "Web",
                ToBase64("""{"Scheme":"TestAuthentication"}"""),
                false),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Validation, result.ErrorCode);
        Assert.Contains("Production", result.Error);
        await _writer.DidNotReceiveWithAnyArgs().UpsertSettingAsync(default, default!, default!, default!, default, default);
    }

    private static string ToBase64(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

    private static TenantConfiguration CreateTenant(Guid id, string name) =>
        new(id, name, new ConfigurationBuilder().Build(), Array.Empty<string>());
}
