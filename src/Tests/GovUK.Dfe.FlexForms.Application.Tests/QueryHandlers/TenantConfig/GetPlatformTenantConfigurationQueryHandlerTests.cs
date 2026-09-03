using GovUK.Dfe.FlexForms.Application.TenantConfig.Queries;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryHandlers.TenantConfig;

public class GetPlatformTenantConfigurationQueryHandlerTests
{
    [Fact]
    public async Task Handle_ShouldReturnTenantConfiguration_WhenTenantExists()
    {
        var tenantId = Guid.NewGuid();
        var reader = Substitute.For<ITenantSettingsReader>();
        reader.GetConfigurationAsync(tenantId, "Web", Arg.Any<CancellationToken>())
            .Returns(new TenantConfigurationSnapshot(
                tenantId,
                "Transfers",
                DateTime.UtcNow,
                new Dictionary<string, string?> { ["DfESignIn:ClientId"] = "test" }));

        var handler = new GetPlatformTenantConfigurationQueryHandler(reader);

        var result = await handler.Handle(
            new GetPlatformTenantConfigurationQuery(tenantId, "Web"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(tenantId, result.Value!.TenantId);
        Assert.Equal("test", result.Value.Configuration["DfESignIn:ClientId"]);
    }

    [Fact]
    public async Task Handle_ShouldExcludeConnectionStrings_WhenTargetIsWeb()
    {
        var tenantId = Guid.NewGuid();
        var reader = Substitute.For<ITenantSettingsReader>();
        reader.GetConfigurationAsync(tenantId, "Web", Arg.Any<CancellationToken>())
            .Returns(new TenantConfigurationSnapshot(
                tenantId,
                "Transfers",
                DateTime.UtcNow,
                new Dictionary<string, string?>
                {
                    ["DfESignIn:ClientId"] = "test",
                    ["ConnectionStrings:Redis"] = "localhost:6379",
                    ["ApplicationInsights:ConnectionString"] = "InstrumentationKey=tenant"
                }));

        var handler = new GetPlatformTenantConfigurationQueryHandler(reader);

        var result = await handler.Handle(
            new GetPlatformTenantConfigurationQuery(tenantId, "Web"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("DfESignIn:ClientId", result.Value!.Configuration.Keys);
        Assert.DoesNotContain(result.Value.Configuration.Keys, key =>
            key.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            "InstrumentationKey=tenant",
            result.Value.Configuration["ApplicationInsights:ConnectionString"]);
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFound_WhenTenantMissing()
    {
        var tenantId = Guid.NewGuid();
        var reader = Substitute.For<ITenantSettingsReader>();
        reader.GetConfigurationAsync(tenantId, "Web", Arg.Any<CancellationToken>())
            .Returns((TenantConfigurationSnapshot?)null);

        var handler = new GetPlatformTenantConfigurationQueryHandler(reader);

        var result = await handler.Handle(
            new GetPlatformTenantConfigurationQuery(tenantId, "Web"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
    }
}
