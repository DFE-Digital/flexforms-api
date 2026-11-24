using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.TenantAdmin.Commands;
using GovUK.Dfe.FlexForms.Application.TenantAdmin.Validation;
using GovUK.Dfe.FlexForms.Application.Tests.TenantAdmin;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using static GovUK.Dfe.FlexForms.Application.Tests.TenantAdmin.TenantAdminTestData;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.TenantAdmin;

public class ValidateTenantSettingCommandHandlerTests
{
    private const string StoredSecret = "stored-client-secret";
    private const string StoredJson = $$"""{"ClientId":"app","ClientSecret":"{{StoredSecret}}"}""";

    private readonly ITenantContextAccessor _tenantContext = Substitute.For<ITenantContextAccessor>();
    private readonly IPermissionCheckerService _permissions = Substitute.For<IPermissionCheckerService>();
    private readonly ITenantSettingsQuery _settings = Substitute.For<ITenantSettingsQuery>();
    private readonly ITemplateHostMappingOwnershipValidator _ownership = Substitute.For<ITemplateHostMappingOwnershipValidator>();
    private readonly IHostEnvironment _environment = Substitute.For<IHostEnvironment>();

    public ValidateTenantSettingCommandHandlerTests()
    {
        _tenantContext.CurrentTenant.Returns(Tenant());
        _environment.EnvironmentName.Returns(Environments.Production);
        _ownership.ValidateAsync(default, default!, default!, default).ReturnsForAnyArgs(Array.Empty<string>());
        Stored();
    }

    [Fact]
    public async Task NotAnInteractiveTenantAdmin_IsForbidden()
    {
        var result = await HandleAsync("Layout", "Web", """{"Theme":"govuk"}""");

        AssertForbidden(result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnotherTenantOrNoTenantContext_IsForbidden(bool noTenantContext)
    {
        AsTenantAdmin();
        if (noTenantContext)
            _tenantContext.CurrentTenant.Returns((TenantConfiguration?)null);

        var result = await HandleAsync("Layout", "Web", """{"Theme":"govuk"}""", tenantId: noTenantContext ? OwnTenantId : OtherTenantId);

        AssertForbidden(result);
    }

    [Theory]
    [MemberData(nameof(SuperAdminOnlyCategories), MemberType = typeof(TenantAdminTestData))]
    public async Task SuperAdminOnlyCategory_IsForbiddenForATenantAdmin(string category)
    {
        AsTenantAdmin();

        var result = await HandleAsync(category, "Shared", "{}");

        AssertForbidden(result);
        Assert.Equal($"Only SuperAdmin can validate '{category}' settings.", result.Error);
    }

    [Fact]
    public async Task SettingsThatAreNotBase64_FailValidation()
    {
        AsTenantAdmin();

        var result = await HandleAsync("Layout", "Web", json: null, rawSettings: "not base64!");

        Assert.Equal(DomainErrorCode.Validation, result.ErrorCode);
    }

    [Fact]
    public async Task NewSetting_IsReportedAsNew()
    {
        AsTenantAdmin();

        var result = await HandleAsync("Layout", "Web", """{"Theme":"govuk"}""");

        Assert.True(result.Value!.IsValid);
        Assert.False(result.Value.SettingExists);
        Assert.Equal("New setting — will be created.", result.Value.DiffSummary);
        Assert.Null(result.Value.CurrentSettingsJson);
        Assert.Equal("""{"Theme":"govuk"}""", result.Value.ProposedSettingsJson);
    }

    [Fact]
    public async Task SameJsonWithDifferentSpacing_HasNoChanges()
    {
        AsTenantAdmin();
        Stored(Row("Layout", "Web", """{"Theme":"govuk"}"""));

        var result = await HandleAsync("layout", "web", """{ "Theme" : "govuk" }""");

        Assert.True(result.Value!.SettingExists);
        Assert.Equal("No JSON content changes detected.", result.Value.DiffSummary);
    }

    [Fact]
    public async Task ChangedJson_ReportsTheSizeChange()
    {
        AsTenantAdmin();
        Stored(Row("Layout", "Web", """{"Theme":"govuk"}"""));

        var result = await HandleAsync("Layout", "Web", """{"Theme":"other"}""");

        Assert.StartsWith("JSON will change (17 → 17 characters).", result.Value!.DiffSummary);
    }

    [Fact]
    public async Task ExistingSecret_IsRedactedForATenantAdmin()
    {
        AsTenantAdmin();
        Stored(Row("Layout", "Web", StoredJson, isSecret: true));

        var result = await HandleAsync("Layout", "Web", $$"""{"ClientId":"app2","ClientSecret":"{{TenantSettingSecretJson.Sentinel}}"}""", isSecret: true);

        Assert.True(result.Value!.IsValid);
        Assert.DoesNotContain(StoredSecret, result.Value.CurrentSettingsJson);
        Assert.DoesNotContain(StoredSecret, result.Value.ProposedSettingsJson);
        Assert.Contains(TenantSettingSecretJson.Sentinel, result.Value.CurrentSettingsJson);
        Assert.Contains("app2", result.Value.ProposedSettingsJson);
    }

    [Fact]
    public async Task NewSecret_IsRedactedForATenantAdmin()
    {
        AsTenantAdmin();

        var result = await HandleAsync("Layout", "Web", """{"ClientSecret":"brand-new"}""", isSecret: true);

        Assert.DoesNotContain("brand-new", result.Value!.ProposedSettingsJson);
    }

    [Fact]
    public async Task ExistingSecret_IsRedactedForASuperAdminInProduction()
    {
        AsSuperAdmin();
        Stored(Row("Layout", "Web", StoredJson, isSecret: true));

        var result = await HandleAsync("Layout", "Web", StoredJson, isSecret: true);

        Assert.DoesNotContain(StoredSecret, result.Value!.CurrentSettingsJson);
        Assert.DoesNotContain(StoredSecret, result.Value.ProposedSettingsJson);
    }

    [Fact]
    public async Task ExistingSecret_IsShownToASuperAdminInDevelopment()
    {
        AsSuperAdmin();
        _environment.EnvironmentName.Returns(Environments.Development);
        Stored(Row("Layout", "Web", StoredJson, isSecret: true));

        var result = await HandleAsync("Layout", "Web", StoredJson, isSecret: true);

        Assert.Contains(StoredSecret, result.Value!.CurrentSettingsJson);
        Assert.Contains(StoredSecret, result.Value.ProposedSettingsJson);
    }

    [Fact]
    public async Task ClearingTheSecretFlag_IsForbiddenForATenantAdmin()
    {
        AsTenantAdmin();
        Stored(Row("Layout", "Web", StoredJson, isSecret: true));

        var result = await HandleAsync("Layout", "Web", StoredJson, isSecret: false);

        AssertForbidden(result);
    }

    [Fact]
    public async Task ClearingTheSecretFlag_IsAllowedForASuperAdmin()
    {
        AsSuperAdmin();
        Stored(Row("Layout", "Web", StoredJson, isSecret: true));

        var result = await HandleAsync("Layout", "Web", StoredJson, isSecret: false);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task SentinelForAPathThatIsNotStored_IsInvalid()
    {
        AsTenantAdmin();
        Stored(Row("Layout", "Web", StoredJson, isSecret: true));

        var result = await HandleAsync("Layout", "Web", $$"""{"ApiKey":"{{TenantSettingSecretJson.Sentinel}}"}""", isSecret: true);

        Assert.False(result.Value!.IsValid);
        Assert.NotEmpty(result.Value.Errors);
        await _ownership.DidNotReceiveWithAnyArgs().ValidateAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task InvalidJson_IsInvalid_WithoutCheckingTemplateOwnership()
    {
        AsTenantAdmin();

        var result = await HandleAsync("Layout", "Web", "{ not json");

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsValid);
        Assert.Contains(result.Value.Errors, e => e.StartsWith("Invalid JSON", StringComparison.Ordinal));
        await _ownership.DidNotReceiveWithAnyArgs().ValidateAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task TemplateOwnershipErrors_MakeItInvalid()
    {
        AsTenantAdmin();
        _ownership.ValidateAsync(OwnTenantId, "Layout", """{"Theme":"govuk"}""", Arg.Any<CancellationToken>())
            .Returns(["Template belongs to another tenant."]);

        var result = await HandleAsync("Layout", "Web", """{"Theme":"govuk"}""");

        Assert.False(result.Value!.IsValid);
        Assert.Equal(["Template belongs to another tenant."], result.Value.Errors);
    }

    private void AsTenantAdmin() => _permissions.IsInteractiveTenantAdmin().Returns(true);

    private void AsSuperAdmin()
    {
        _permissions.IsInteractiveTenantAdmin().Returns(true);
        _permissions.IsInteractivePlatformAdmin().Returns(true);
    }

    private void Stored(params TenantSettingRow[] rows) =>
        _settings.ListSettingsAsync(OwnTenantId, Arg.Any<CancellationToken>())
            .Returns(new TenantSettingsList(OwnTenantId, "Transfers", rows));

    private void AssertForbidden(Result<ValidateTenantSettingResponse> result)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
    }

    private Task<Result<ValidateTenantSettingResponse>> HandleAsync(
        string category,
        string target,
        string? json,
        bool isSecret = false,
        Guid? tenantId = null,
        string? rawSettings = null)
    {
        var handler = new ValidateTenantSettingCommandHandler(_tenantContext, _permissions, _settings, _ownership, _environment);
        return handler.Handle(
            new ValidateTenantSettingCommand(tenantId ?? OwnTenantId, category, target, rawSettings ?? ToBase64(json ?? ""), isSecret),
            CancellationToken.None);
    }
}
