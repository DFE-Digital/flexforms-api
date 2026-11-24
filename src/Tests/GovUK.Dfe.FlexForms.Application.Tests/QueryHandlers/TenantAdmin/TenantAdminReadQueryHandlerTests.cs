using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.TenantAdmin.Queries;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using NSubstitute;
using static GovUK.Dfe.FlexForms.Application.Tests.TenantAdmin.TenantAdminTestData;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryHandlers.TenantAdmin;

/// <summary>Export, effective configuration, platform tenants and the setting audit log: who may read them.</summary>
public class TenantAdminReadQueryHandlerTests
{
    private readonly ITenantContextAccessor _tenantContext = Substitute.For<ITenantContextAccessor>();
    private readonly IPermissionCheckerService _permissions = Substitute.For<IPermissionCheckerService>();
    private readonly ITenantConfigurationPromotion _promotion = Substitute.For<ITenantConfigurationPromotion>();
    private readonly ITenantSettingAuditQuery _auditQuery = Substitute.For<ITenantSettingAuditQuery>();
    private readonly ITenantConfigurationProvider _configProvider = Substitute.For<ITenantConfigurationProvider>();
    private readonly ITenantConfigurationRefreshState _refreshState = Substitute.For<ITenantConfigurationRefreshState>();
    private readonly ITenantAuthProviderRegistry _authProviders = Substitute.For<ITenantAuthProviderRegistry>();
    private readonly ITenantHostnameResolver _hostnames = Substitute.For<ITenantHostnameResolver>();
    private readonly ITenantSettingsReader _settingsReader = Substitute.For<ITenantSettingsReader>();

    private static readonly DateTimeOffset LastRefreshed = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    public TenantAdminReadQueryHandlerTests()
    {
        _tenantContext.CurrentTenant.Returns(Tenant(frontendOrigins: ["https://apply.example.gov.uk"]));
        _hostnames.ListHostnamesForTenantAsync(default, default).ReturnsForAnyArgs(["apply.example.gov.uk"]);
        _configProvider.Source.Returns("Database");
        _refreshState.LastRefreshedUtc.Returns(LastRefreshed);
        _refreshState.ActiveTenantCount.Returns(3);
        _authProviders.GetAll().Returns([Provider(), Provider()]);
    }

    // ---- Export ----

    [Fact]
    public async Task Export_SuperAdminExportsTheirOwnTenant()
    {
        _permissions.IsInteractivePlatformAdmin().Returns(true);
        var export = new ExportTenantConfigurationDto(OwnTenantId, "Transfers", LastRefreshed, []);
        _promotion.ExportAsync(OwnTenantId, Arg.Any<CancellationToken>()).Returns(export);

        var result = await Export(OwnTenantId);

        Assert.Same(export, result.Value);
    }

    [Fact]
    public async Task Export_TenantAdminIsForbidden()
    {
        _permissions.IsInteractiveTenantAdmin().Returns(true);

        var result = await Export(OwnTenantId);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _promotion.DidNotReceiveWithAnyArgs().ExportAsync(default, default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Export_AnotherTenantOrNoTenantContextIsForbidden(bool noTenantContext)
    {
        _permissions.IsInteractivePlatformAdmin().Returns(true);
        if (noTenantContext)
            _tenantContext.CurrentTenant.Returns((TenantConfiguration?)null);

        var result = await Export(noTenantContext ? OwnTenantId : OtherTenantId);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _promotion.DidNotReceiveWithAnyArgs().ExportAsync(default, default);
    }

    [Fact]
    public async Task Export_UnknownTenantIsNotFound()
    {
        _permissions.IsInteractivePlatformAdmin().Returns(true);
        _promotion.ExportAsync(OwnTenantId, Arg.Any<CancellationToken>()).Returns((ExportTenantConfigurationDto?)null);

        var result = await Export(OwnTenantId);

        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
    }

    // ---- Effective configuration ----

    [Fact]
    public async Task EffectiveConfig_TenantAdminSeesTheirTenantWithoutPlatformDetails()
    {
        _permissions.IsInteractiveTenantAdmin().Returns(true);
        WebSettings(new Dictionary<string, string?>
        {
            ["TestAuthentication:Enabled"] = "true",
            ["EntraSso:Enabled"] = "false"
        });

        var result = await EffectiveConfig(OwnTenantId);

        var dto = result.Value!;
        Assert.Equal("Transfers", dto.TenantName);
        Assert.Equal("Database", dto.ConfigSource);
        Assert.True(dto.TestAuthenticationEnabled);
        Assert.False(dto.EntraSsoEnabled);
        Assert.Equal(["apply.example.gov.uk"], dto.Hostnames);
        Assert.Null(dto.LastCatalogueRefreshUtc);
        Assert.Equal(0, dto.ActiveTenantCount);
        Assert.Equal(0, dto.RegisteredAuthProviderCount);
        Assert.Empty(dto.FrontendOrigins);
    }

    [Fact]
    public async Task EffectiveConfig_SuperAdminAlsoSeesPlatformDetails()
    {
        _permissions.IsInteractivePlatformAdmin().Returns(true);

        var result = await EffectiveConfig(OwnTenantId);

        var dto = result.Value!;
        Assert.Equal(LastRefreshed, dto.LastCatalogueRefreshUtc);
        Assert.Equal(3, dto.ActiveTenantCount);
        Assert.Equal(2, dto.RegisteredAuthProviderCount);
        Assert.Equal(["https://apply.example.gov.uk"], dto.FrontendOrigins);
    }

    [Fact]
    public async Task EffectiveConfig_SuperAdminWithoutRefreshStateCountsTheLoadedTenants()
    {
        _permissions.IsInteractivePlatformAdmin().Returns(true);
        _configProvider.GetAllTenants().Returns([Tenant(), Tenant(OtherTenantId)]);

        var result = await EffectiveConfig(OwnTenantId, withRefreshState: false);

        Assert.Equal(2, result.Value!.ActiveTenantCount);
        Assert.Null(result.Value.LastCatalogueRefreshUtc);
    }

    [Fact]
    public async Task EffectiveConfig_NonAdminIsForbidden()
    {
        var result = await EffectiveConfig(OwnTenantId);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EffectiveConfig_AnotherTenantOrNoTenantContextIsForbidden(bool noTenantContext)
    {
        _permissions.IsInteractivePlatformAdmin().Returns(true);
        if (noTenantContext)
            _tenantContext.CurrentTenant.Returns((TenantConfiguration?)null);

        var result = await EffectiveConfig(noTenantContext ? OwnTenantId : OtherTenantId);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _settingsReader.DidNotReceiveWithAnyArgs().GetConfigurationAsync(default, default!, default);
    }

    // ---- Platform tenants ----

    [Fact]
    public async Task PlatformTenants_SuperAdminSeesEveryTenantByName()
    {
        _permissions.IsInteractivePlatformAdmin().Returns(true);
        _configProvider.GetAllTenants().Returns([Tenant(OtherTenantId, "visits"), Tenant(OwnTenantId, "Academies")]);

        var result = await new GetPlatformTenantsQueryHandler(_configProvider, _refreshState, _permissions, _hostnames)
            .Handle(new GetPlatformTenantsQuery(), CancellationToken.None);

        var response = result.Value!;
        Assert.Equal("Database", response.Source);
        Assert.Equal(2, response.TenantCount);
        Assert.Equal(LastRefreshed, response.LastCatalogueRefreshUtc);
        Assert.Equal(["Academies", "visits"], response.Tenants.Select(t => t.TenantName));
        Assert.All(response.Tenants, t => Assert.Equal(["apply.example.gov.uk"], t.Hostnames));
    }

    [Fact]
    public async Task PlatformTenants_TenantAdminIsForbidden()
    {
        _permissions.IsInteractiveTenantAdmin().Returns(true);

        var result = await new GetPlatformTenantsQueryHandler(_configProvider, null, _permissions, _hostnames)
            .Handle(new GetPlatformTenantsQuery(), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        _configProvider.DidNotReceive().GetAllTenants();
    }

    // ---- Setting audit log ----

    [Fact]
    public async Task AuditLog_TenantAdminReadsTheirOwnTenant()
    {
        _permissions.IsInteractiveTenantAdmin().Returns(true);
        var log = new GetTenantSettingAuditLogDto(OwnTenantId, []);
        _auditQuery.ListAsync(OwnTenantId, 25, Arg.Any<CancellationToken>()).Returns(log);

        var result = await AuditLog(OwnTenantId, take: 25);

        Assert.Same(log, result.Value);
    }

    [Fact]
    public async Task AuditLog_NotAnInteractiveTenantAdminIsForbidden()
    {
        _permissions.IsInteractivePlatformAdmin().Returns(true);

        var result = await AuditLog(OwnTenantId);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuditLog_AnotherTenantOrNoTenantContextIsForbidden(bool noTenantContext)
    {
        _permissions.IsInteractiveTenantAdmin().Returns(true);
        if (noTenantContext)
            _tenantContext.CurrentTenant.Returns((TenantConfiguration?)null);

        var result = await AuditLog(noTenantContext ? OwnTenantId : OtherTenantId);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _auditQuery.DidNotReceiveWithAnyArgs().ListAsync(default, default, default);
    }

    [Fact]
    public async Task AuditLog_UnknownTenantIsNotFound()
    {
        _permissions.IsInteractiveTenantAdmin().Returns(true);
        _auditQuery.ListAsync(OwnTenantId, 100, Arg.Any<CancellationToken>()).Returns((GetTenantSettingAuditLogDto?)null);

        var result = await AuditLog(OwnTenantId);

        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
    }

    private Task<Result<ExportTenantConfigurationDto>> Export(Guid tenantId) =>
        new ExportTenantConfigurationQueryHandler(_tenantContext, _permissions, _promotion)
            .Handle(new ExportTenantConfigurationQuery(tenantId), CancellationToken.None);

    private Task<Result<TenantEffectiveConfigurationDto>> EffectiveConfig(Guid tenantId, bool withRefreshState = true) =>
        new GetTenantEffectiveConfigurationQueryHandler(
                _tenantContext,
                _permissions,
                _configProvider,
                withRefreshState ? _refreshState : null,
                _authProviders,
                _hostnames,
                _settingsReader)
            .Handle(new GetTenantEffectiveConfigurationQuery(tenantId), CancellationToken.None);

    private Task<Result<GetTenantSettingAuditLogDto>> AuditLog(Guid tenantId, int take = 100) =>
        new GetTenantSettingAuditLogQueryHandler(_tenantContext, _permissions, _auditQuery)
            .Handle(new GetTenantSettingAuditLogQuery(tenantId, take), CancellationToken.None);

    private void WebSettings(IReadOnlyDictionary<string, string?> settings) =>
        _settingsReader.GetConfigurationAsync(OwnTenantId, "Web", Arg.Any<CancellationToken>())
            .Returns(new TenantConfigurationSnapshot(OwnTenantId, "Transfers", DateTime.UtcNow, settings));

    private static TenantAuthProvider Provider() =>
        new(OwnTenantId, "provider", default, false, "https://issuer", "https://issuer", "", [], "client", "", "", "", []);
}
