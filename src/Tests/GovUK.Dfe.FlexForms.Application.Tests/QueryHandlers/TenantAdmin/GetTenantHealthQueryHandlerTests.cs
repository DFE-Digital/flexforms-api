using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.TenantAdmin.Queries;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryHandlers.TenantAdmin;

public class GetTenantHealthQueryHandlerTests
{
    private readonly ITenantContextAccessor _tenantContext = Substitute.For<ITenantContextAccessor>();
    private readonly IPermissionCheckerService _permissionChecker = Substitute.For<IPermissionCheckerService>();
    private readonly ITenantConfigurationProvider _configProvider = Substitute.For<ITenantConfigurationProvider>();
    private readonly ITenantConfigurationRefreshState _refreshState = Substitute.For<ITenantConfigurationRefreshState>();
    private readonly ITenantAuthProviderRegistry _authRegistry = Substitute.For<ITenantAuthProviderRegistry>();
    private readonly ITenantHostnameResolver _hostnameResolver = Substitute.For<ITenantHostnameResolver>();
    private readonly ITenantSettingsReader _settingsReader = Substitute.For<ITenantSettingsReader>();
    private readonly ITenantSettingsQuery _settingsQuery = Substitute.For<ITenantSettingsQuery>();
    private readonly GetTenantHealthQueryHandler _handler;

    public GetTenantHealthQueryHandlerTests()
    {
        _handler = new GetTenantHealthQueryHandler(
            _tenantContext,
            _permissionChecker,
            _configProvider,
            _refreshState,
            _authRegistry,
            _hostnameResolver,
            _settingsReader,
            _settingsQuery);

        _configProvider.Source.Returns("Database");
        _refreshState.LastRefreshedUtc.Returns(DateTimeOffset.UtcNow);
        _refreshState.ActiveTenantCount.Returns(42);
        _authRegistry.GetAll().Returns(Array.Empty<TenantAuthProvider>());
    }

    [Fact]
    public async Task Handle_ShouldForbid_WhenRouteTenantDoesNotMatchCurrentTenant()
    {
        var ownId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var otherId = Guid.Parse("22222222-2222-4222-8222-222222222222");

        _permissionChecker.IsInteractiveTenantAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(CreateTenant(ownId, "Transfers", ["https://a.example"]));

        var result = await _handler.Handle(new GetTenantHealthQuery(otherId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_TenantAdmin_ShouldRedactPlatformMetadata()
    {
        var tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var origins = new[] { "https://transfers.example", "https://other.example" };

        _permissionChecker.IsInteractiveTenantAdmin().Returns(true);
        _permissionChecker.IsInteractivePlatformAdmin().Returns(false);
        _tenantContext.CurrentTenant.Returns(CreateTenant(tenantId, "Transfers", origins));

        _settingsReader.GetConfigurationAsync(tenantId, "Web", Arg.Any<CancellationToken>())
            .Returns(new TenantConfigurationSnapshot(
                tenantId, "Transfers", DateTime.UtcNow,
                new Dictionary<string, string?>()));

        _hostnameResolver.ListHostnamesForTenantAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(["transfers.example"]);

        _settingsQuery.ListSettingsAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new TenantSettingsList(tenantId, "Transfers", []));

        var result = await _handler.Handle(new GetTenantHealthQuery(tenantId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var effective = result.Value!.EffectiveConfiguration;
        Assert.Null(effective.LastCatalogueRefreshUtc);
        Assert.Equal(0, effective.ActiveTenantCount);
        Assert.Equal(0, effective.RegisteredAuthProviderCount);
        Assert.Empty(effective.FrontendOrigins);

        var cors = Assert.Single(result.Value.Checks, c => c.Code == "cors");
        Assert.DoesNotContain("https://", cors.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2 origin", cors.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_SuperAdmin_ShouldIncludePlatformMetadata()
    {
        var tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var origins = new[] { "https://transfers.example" };

        _permissionChecker.IsInteractiveTenantAdmin().Returns(true);
        _permissionChecker.IsInteractivePlatformAdmin().Returns(true);
        _tenantContext.CurrentTenant.Returns(CreateTenant(tenantId, "Transfers", origins));

        _settingsReader.GetConfigurationAsync(tenantId, "Web", Arg.Any<CancellationToken>())
            .Returns(new TenantConfigurationSnapshot(
                tenantId, "Transfers", DateTime.UtcNow,
                new Dictionary<string, string?>()));

        _hostnameResolver.ListHostnamesForTenantAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(["transfers.example"]);

        _settingsQuery.ListSettingsAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new TenantSettingsList(tenantId, "Transfers",
            [
                new TenantSettingRow(Guid.NewGuid(), "Layout", "Web", "{}", false, DateTime.UtcNow)
            ]));

        var result = await _handler.Handle(new GetTenantHealthQuery(tenantId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var effective = result.Value!.EffectiveConfiguration;
        Assert.NotNull(effective.LastCatalogueRefreshUtc);
        Assert.Equal(42, effective.ActiveTenantCount);
        Assert.Equal(origins, effective.FrontendOrigins);
    }

    private static TenantConfiguration CreateTenant(Guid id, string name, string[]? origins = null) =>
        new(id, name, new ConfigurationBuilder().Build(), origins ?? []);
}
