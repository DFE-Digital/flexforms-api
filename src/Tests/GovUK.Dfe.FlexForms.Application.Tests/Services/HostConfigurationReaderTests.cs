using GovUK.Dfe.FlexForms.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class HostConfigurationReaderTests
{
    [Fact]
    public void GetConfiguration_ShouldFlattenGlobalConfiguration_AndAllowListedConnectionStrings()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GlobalConfiguration:ApplicationInsights:ConnectionString"] = "InstrumentationKey=test",
                ["GlobalConfiguration:Email:GovUkNotify:ApiKey"] = "must-not-export",
                ["GlobalConfiguration:Email:GovUkNotify:BaseUrl"] = "https://api.notifications.service.gov.uk",
                ["ConnectionStrings:Redis"] = "localhost:6379",
                ["ConnectionStrings:TenantConfigDatabase"] = "must-not-export",
                ["ConnectionStrings:ServiceBus"] = "must-not-export",
                ["Logging:LogLevel:Default"] = "Information"
            })
            .Build();

        var reader = new HostConfigurationReader(configuration);

        var snapshot = reader.GetConfiguration("Web");

        Assert.Equal("Web", snapshot.Target);
        Assert.Equal("https://api.notifications.service.gov.uk", snapshot.Configuration["Email:GovUkNotify:BaseUrl"]);
        Assert.False(snapshot.Configuration.ContainsKey("Email:GovUkNotify:ApiKey"));
        Assert.Equal("localhost:6379", snapshot.Configuration["ConnectionStrings:Redis"]);
        Assert.Equal("Information", snapshot.Configuration["LogLevel:Default"]);
        Assert.False(snapshot.Configuration.ContainsKey("ConnectionStrings:TenantConfigDatabase"));
        Assert.False(snapshot.Configuration.ContainsKey("ConnectionStrings:ServiceBus"));
    }

    [Theory]
    [InlineData("Web")]
    [InlineData("web")]
    public void GetConfiguration_ShouldNotExportApplicationInsightsDestination_ForWeb(string target)
    {
        var reader = new HostConfigurationReader(CreateApplicationInsightsConfiguration());

        var snapshot = reader.GetConfiguration(target);

        Assert.False(snapshot.Configuration.ContainsKey("ApplicationInsights:ConnectionString"));
        Assert.False(snapshot.Configuration.ContainsKey("ApplicationInsights:InstrumentationKey"));
        Assert.Equal(".example.gov.uk", snapshot.Configuration["ApplicationInsights:CookieDomain"]);
        Assert.Equal("https://api.notifications.service.gov.uk", snapshot.Configuration["Email:GovUkNotify:BaseUrl"]);
    }

    [Fact]
    public void GetConfiguration_ShouldExportApplicationInsightsDestination_ForApi()
    {
        var reader = new HostConfigurationReader(CreateApplicationInsightsConfiguration());

        var snapshot = reader.GetConfiguration("Api");

        Assert.Equal("InstrumentationKey=test", snapshot.Configuration["ApplicationInsights:ConnectionString"]);
        Assert.Equal("legacy-key", snapshot.Configuration["ApplicationInsights:InstrumentationKey"]);
        Assert.Equal(".example.gov.uk", snapshot.Configuration["ApplicationInsights:CookieDomain"]);
    }

    private static IConfiguration CreateApplicationInsightsConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GlobalConfiguration:ApplicationInsights:ConnectionString"] = "InstrumentationKey=test",
                ["GlobalConfiguration:ApplicationInsights:InstrumentationKey"] = "legacy-key",
                ["GlobalConfiguration:ApplicationInsights:CookieDomain"] = ".example.gov.uk",
                ["GlobalConfiguration:Email:GovUkNotify:BaseUrl"] = "https://api.notifications.service.gov.uk"
            })
            .Build();
}
