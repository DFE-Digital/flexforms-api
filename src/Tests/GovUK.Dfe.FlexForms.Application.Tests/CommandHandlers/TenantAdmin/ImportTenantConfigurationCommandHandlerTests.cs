using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.TenantAdmin.Commands;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using static GovUK.Dfe.FlexForms.Application.Tests.TenantAdmin.TenantAdminTestData;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.TenantAdmin;

public class ImportTenantConfigurationCommandHandlerTests
{
    private const string SecretPlaceholder = """{"__SECRET__":true}""";

    private readonly ITenantContextAccessor _tenantContext = Substitute.For<ITenantContextAccessor>();
    private readonly IPermissionCheckerService _permissions = Substitute.For<IPermissionCheckerService>();
    private readonly ITenantConfigurationPromotion _promotion = Substitute.For<ITenantConfigurationPromotion>();
    private readonly ITenantConfigurationProvider _configProvider = Substitute.For<ITenantConfigurationProvider>();
    private readonly ImportTenantConfigurationCommandHandler _handler;

    public ImportTenantConfigurationCommandHandlerTests()
    {
        _tenantContext.CurrentTenant.Returns(Tenant());
        _promotion.ImportAsync(default, default!, default!, default)
            .ReturnsForAnyArgs(new ImportTenantConfigurationResultDto(1, 0, []));
        _handler = new ImportTenantConfigurationCommandHandler(_tenantContext, _permissions, _promotion, _configProvider, SignedInAs());
    }

    [Fact]
    public async Task SuperAdmin_ImportsIntoTheirOwnTenant()
    {
        _permissions.IsInteractivePlatformAdmin().Returns(true);
        var bundle = Bundle(Item("Dashboard", "Web", """{"EnableFilters":true}"""));

        var result = await _handler.Handle(new ImportTenantConfigurationCommand(OwnTenantId, bundle), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await _promotion.Received(1).ImportAsync(OwnTenantId, bundle, ActorEmail, Arg.Any<CancellationToken>());
        await _configProvider.Received(1).RefreshAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TenantAdmin_IsForbidden()
    {
        _permissions.IsInteractiveTenantAdmin().Returns(true);

        var result = await _handler.Handle(new ImportTenantConfigurationCommand(OwnTenantId, Bundle(Item("Dashboard", "Web", "{}"))), CancellationToken.None);

        AssertRefused(result, DomainErrorCode.Forbidden);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnotherTenantOrNoTenantContext_IsForbidden(bool noTenantContext)
    {
        _permissions.IsInteractivePlatformAdmin().Returns(true);
        if (noTenantContext)
            _tenantContext.CurrentTenant.Returns((TenantConfiguration?)null);

        var result = await _handler.Handle(
            new ImportTenantConfigurationCommand(noTenantContext ? OwnTenantId : OtherTenantId, Bundle(Item("Dashboard", "Web", "{}"))),
            CancellationToken.None);

        AssertRefused(result, DomainErrorCode.Forbidden);
    }

    [Fact]
    public async Task EmptyBundle_FailsValidation()
    {
        _permissions.IsInteractivePlatformAdmin().Returns(true);

        var result = await _handler.Handle(new ImportTenantConfigurationCommand(OwnTenantId, Bundle()), CancellationToken.None);

        AssertRefused(result, DomainErrorCode.Validation);
        Assert.Equal("At least one setting is required.", result.Error);
    }

    [Fact]
    public async Task UnparseableJson_FailsValidation_NamingTheSetting()
    {
        _permissions.IsInteractivePlatformAdmin().Returns(true);

        var result = await _handler.Handle(
            new ImportTenantConfigurationCommand(OwnTenantId, Bundle(Item("Dashboard", "Web", "{ not json"))),
            CancellationToken.None);

        AssertRefused(result, DomainErrorCode.Validation);
        Assert.StartsWith("Dashboard/Web: Invalid JSON", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SecretPlaceholders_PassValidation(bool skipSecretPlaceholders)
    {
        _permissions.IsInteractivePlatformAdmin().Returns(true);
        var bundle = Bundle(skipSecretPlaceholders, Item("ConnectionStrings", "Shared", SecretPlaceholder, isSecret: true));

        var result = await _handler.Handle(new ImportTenantConfigurationCommand(OwnTenantId, bundle), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await _promotion.Received(1).ImportAsync(OwnTenantId, bundle, ActorEmail, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TargetConflict_FailsValidation_WithoutRefreshing()
    {
        _permissions.IsInteractivePlatformAdmin().Returns(true);
        _promotion.ImportAsync(default, default!, default!, default)
            .ThrowsAsyncForAnyArgs(new TenantSettingTargetConflictException("Overlap."));

        var result = await _handler.Handle(
            new ImportTenantConfigurationCommand(OwnTenantId, Bundle(Item("FileValidation", "Web", "{}"))),
            CancellationToken.None);

        Assert.Equal(DomainErrorCode.Validation, result.ErrorCode);
        await _configProvider.DidNotReceiveWithAnyArgs().RefreshAsync(default);
    }

    private void AssertRefused(Result<ImportTenantConfigurationResultDto> result, DomainErrorCode code)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.ErrorCode);
        _promotion.DidNotReceiveWithAnyArgs().ImportAsync(default, default!, default!, default);
        _configProvider.DidNotReceiveWithAnyArgs().RefreshAsync(default);
    }

    private static ImportTenantConfigurationDto Bundle(params TenantSettingImportItemDto[] items) => Bundle(true, items);

    private static ImportTenantConfigurationDto Bundle(bool skipSecretPlaceholders, params TenantSettingImportItemDto[] items) =>
        new(items, skipSecretPlaceholders);

    private static TenantSettingImportItemDto Item(string category, string target, string json, bool isSecret = false) =>
        new(category, target, json, isSecret);
}
