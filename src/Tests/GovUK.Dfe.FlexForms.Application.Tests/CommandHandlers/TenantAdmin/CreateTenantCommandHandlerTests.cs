using System.Text.Json;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Common;
using GovUK.Dfe.FlexForms.Application.TenantAdmin.Commands;
using GovUK.Dfe.FlexForms.Application.TenantAdmin.Validation;
using GovUK.Dfe.FlexForms.Application.Tests.TenantAdmin;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.TenantAdmin;

public class CreateTenantCommandHandlerTests
{
    private static readonly Guid SourceId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid NewId = Guid.Parse("33333333-3333-4333-8333-333333333333");

    private const string SourceConnectionJson = """{"DefaultConnection":"Server=source;Database=ea;"}""";

    private readonly ITenantCreator _creator = Substitute.For<ITenantCreator>();
    private readonly ITenantSettingsQuery _settingsQuery = Substitute.For<ITenantSettingsQuery>();
    private readonly ITenantContextAccessor _tenantContext = Substitute.For<ITenantContextAccessor>();
    private readonly IPermissionCheckerService _permissionChecker = Substitute.For<IPermissionCheckerService>();
    private readonly ITenantConfigurationProvider _configProvider = Substitute.For<ITenantConfigurationProvider>();
    private readonly IHostEnvironment _hostEnvironment = Substitute.For<IHostEnvironment>();
    private readonly ITemplateHostMappingOwnershipValidator _ownershipValidator = Substitute.For<ITemplateHostMappingOwnershipValidator>();
    private readonly CreateTenantCommandHandler _handler;

    public CreateTenantCommandHandlerTests()
    {
        _hostEnvironment.EnvironmentName.Returns("Development");
        _permissionChecker.IsInteractivePlatformAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(
            new TenantConfiguration(
                SourceId,
                "Transfers",
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = "Server=db;Database=ea;"
                    })
                    .Build(),
                []));
        _settingsQuery.ListSettingsAsync(SourceId, Arg.Any<CancellationToken>())
            .Returns(new TenantSettingsList(SourceId, "Transfers",
            [
                new TenantSettingRow(Guid.NewGuid(), "ConnectionStrings", "Api", SourceConnectionJson, true, DateTime.UtcNow)
            ]));
        _creator.CreateAsync(Arg.Any<NewTenantDefinition>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var tenant = ci.Arg<NewTenantDefinition>();
                return new CreateTenantResult(tenant.TenantId, tenant.Name, tenant.Hostname, tenant.FrontendOrigin, tenant.Settings.Count);
            });
        _ownershipValidator
            .ValidateAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());

        _handler = new CreateTenantCommandHandler(
            _creator,
            _settingsQuery,
            _tenantContext,
            _permissionChecker,
            _configProvider,
            _ownershipValidator,
            _hostEnvironment);
    }

    [Fact]
    public async Task Handle_ShouldAcceptOptionalCopiedCategories()
    {
        var settings = MinimalSettings()
            .Append(Row("FeatureManagement", "Web", """{"TemplateStudio":true}"""))
            .Append(Row("FormEngine", "Web", """{"ComplexFields":{}}"""))
            .ToList();

        var result = await _handler.Handle(Command(settings), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(8, result.Value!.SettingsCopied);
    }

    [Fact]
    public async Task Handle_ShouldReject_CopiedTemplateMappingsTheNewTenantCannotClaim()
    {
        var templateId = Guid.NewGuid();
        _ownershipValidator
            .ValidateAsync(NewId, "ApplicationTemplates", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([$"Template '{templateId}' belongs to another tenant and cannot be mapped."]);
        var settings = MinimalSettings()
            .Append(Row("ApplicationTemplates", "Api", $$$"""{"HostMappings":{"transfers":"{{{templateId}}}"}}"""))
            .ToList();

        var result = await _handler.Handle(Command(settings), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Validation, result.ErrorCode);
        Assert.Contains("belongs to another tenant", result.Error);
        await _creator.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    [Fact]
    public async Task Handle_ShouldDeferTemplateMappings_WhenNewTenantUsesAnotherDatabase()
    {
        var settings = MinimalSettings()
            .Where(s => s.Category != "ConnectionStrings")
            .Append(Row("ConnectionStrings", "Api", """{"DefaultConnection":"Server=isolated;Database=ea;"}"""))
            .Append(Row("ApplicationTemplates", "Api", $$$"""{"HostMappings":{"transfers":"{{{Guid.NewGuid()}}}"}}"""))
            .ToList();

        var result = await _handler.Handle(Command(settings), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Validation, result.ErrorCode);
        Assert.Contains("current tenant's application database", result.Error);
        await _ownershipValidator.DidNotReceiveWithAnyArgs().ValidateAsync(default, default!, default!, default);
    }

    [Theory]
    [InlineData("__SECRET__")]
    [InlineData("""{"__SECRET__":true}""")]
    public async Task Handle_ShouldReject_UnresolvedSecretPlaceholder(string json)
    {
        var settings = MinimalSettings().Append(Row("FileStorage", "Api", json)).ToList();

        var result = await _handler.Handle(Command(settings), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Validation, result.ErrorCode);
        Assert.Contains("Replace __SECRET__", result.Error);
        await _creator.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    [Fact]
    public async Task Handle_ShouldReturnValidation_WhenSettingsContainNull()
    {
        var payload = """{"hostname":"new.dev.example","frontendOrigin":"https://new.dev.example","settings":[null]}""";

        var result = await _handler.Handle(
            new CreateTenantCommand(SourceId, NewId, "New tenant", WafSafeUtf8Base64.Encode(payload)),
            CancellationToken.None);

        Assert.Equal(DomainErrorCode.Validation, result.ErrorCode);
        Assert.Contains("null entries", result.Error);
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenCallerIsNotSuperAdmin()
    {
        _permissionChecker.IsInteractivePlatformAdmin().Returns(false);

        var result = await _handler.Handle(Command(MinimalSettings()), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _creator.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenSourceIsNotCurrentTenant()
    {
        var command = Command(MinimalSettings()) with { SourceTenantId = Guid.NewGuid() };

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _creator.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    [Fact]
    public async Task Handle_ShouldCreateOnlySubmittedRows_AndRestoreRedactedFromCurrentTenant()
    {
        var settings = MinimalSettings()
            .Where(s => s.Category != "ConnectionStrings")
            .Append(Row("ConnectionStrings", "Api", """{"DefaultConnection":"__REDACTED__"}"""))
            .ToList();

        var result = await _handler.Handle(Command(settings), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(6, result.Value!.SettingsCopied);
        await _creator.Received(1).CreateAsync(
            Arg.Is<NewTenantDefinition>(t =>
                t.TenantId == NewId
                && t.Settings.Count == 6
                && t.Settings.Single(s => s.Category == "ConnectionStrings").SettingsJson.Contains("Server=source")
                && t.Settings.All(s => !s.SettingsJson.Contains("__REDACTED__"))
                && t.Settings.Single(s => s.Category == "ConnectionStrings").IsSecret
                && !t.Settings.Single(s => s.Category == "Authentication").IsSecret),
            Arg.Any<CancellationToken>());
        await _configProvider.Received(1).RefreshAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldReject_WhenRedactedValueHasNoMatchingCurrentTenantRow()
    {
        var settings = MinimalSettings()
            .Where(s => s.Category != "AzureAd")
            .Append(Row("AzureAd", "Api", """{"TenantId":"t","ClientSecret":"__REDACTED__"}"""))
            .ToList();

        var result = await _handler.Handle(Command(settings), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Validation, result.ErrorCode);
        Assert.Contains("no AzureAd (Api) setting to copy it from", result.Error);
        await _creator.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    [Fact]
    public async Task Handle_ShouldReject_CategoriesOutsideTheMinimum()
    {
        var settings = MinimalSettings().Append(Row("InternalServiceAuth", "Api", """{"Issuer":"x"}""")).ToList();

        var result = await _handler.Handle(Command(settings), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Validation, result.ErrorCode);
        Assert.Contains("'InternalServiceAuth' (Api) cannot be set when creating a tenant", result.Error);
    }

    [Fact]
    public async Task Handle_ShouldReject_WhenMinimumIsNotMet()
    {
        var settings = MinimalSettings().Where(s => s.Category != "AzureAd").ToList();

        var result = await _handler.Handle(Command(settings), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Validation, result.ErrorCode);
        Assert.Contains("AzureAd", result.Error);
    }

    [Fact]
    public async Task Handle_ShouldReject_TestAuthenticationInProduction()
    {
        _hostEnvironment.EnvironmentName.Returns("Production");
        var settings = MinimalSettings()
            .Where(s => s.Category != "Authentication")
            .Append(Row("Authentication", "Web", """{"Scheme":"TestAuthentication"}"""))
            .ToList();

        var result = await _handler.Handle(Command(settings), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Validation, result.ErrorCode);
        Assert.Contains("cannot be enabled or selected in Production", result.Error);
    }

    [Fact]
    public async Task Handle_ShouldReturnValidation_WhenPayloadIsNotBase64()
    {
        var result = await _handler.Handle(
            new CreateTenantCommand(SourceId, NewId, "New tenant", "not-valid-base64!!!"),
            CancellationToken.None);

        Assert.Equal(DomainErrorCode.Validation, result.ErrorCode);
        Assert.Contains("PayloadJson", result.Error);
    }

    [Fact]
    public async Task Handle_ShouldReturnValidation_WhenCreatorRejects()
    {
        _creator.CreateAsync(Arg.Any<NewTenantDefinition>(), Arg.Any<CancellationToken>())
            .Returns<Task<CreateTenantResult>>(_ => throw new InvalidOperationException("Hostname already assigned"));

        var result = await _handler.Handle(Command(MinimalSettings()), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Validation, result.ErrorCode);
        Assert.Contains("Hostname", result.Error);
    }

    private static List<NewTenantPayloadSetting> MinimalSettings() =>
        NewTenantSettingsRequirementsTests.DfESignInTenant()
            .Select(s => Row(s.Category, s.Target, s.SettingsJson))
            .ToList();

    private static NewTenantPayloadSetting Row(string category, string target, string json) =>
        new() { Category = category, Target = target, SettingsJson = json };

    private static CreateTenantCommand Command(List<NewTenantPayloadSetting> settings)
    {
        var payload = new NewTenantPayload
        {
            Hostname = "new.dev.example",
            FrontendOrigin = "https://new.dev.example",
            Settings = settings
        };

        return new CreateTenantCommand(
            SourceId,
            NewId,
            "New tenant",
            WafSafeUtf8Base64.Encode(JsonSerializer.Serialize(payload)));
    }
}
