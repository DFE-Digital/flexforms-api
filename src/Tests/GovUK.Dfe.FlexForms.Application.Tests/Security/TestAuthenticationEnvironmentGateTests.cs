using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Security;

public class TestAuthenticationEnvironmentGateTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Local")]
    [InlineData("Test")]
    public void IsAllowed_ReturnsTrue_OnAllowList(string environmentName)
    {
        Assert.True(TestAuthenticationEnvironmentGate.IsAllowed(environmentName));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("production")]
    [InlineData("Prod")]
    [InlineData("PROD")]
    [InlineData("Staging")]
    [InlineData("prd")]
    [InlineData("Live")]
    [InlineData(null)]
    [InlineData("")]
    public void IsAllowed_ReturnsFalse_OffAllowList(string? environmentName)
    {
        Assert.False(TestAuthenticationEnvironmentGate.IsAllowed(environmentName));
    }

    [Fact]
    public void IsProduction_ReturnsTrue_ForProductionHostEnvironment()
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(Environments.Production);

        Assert.True(TestAuthenticationEnvironmentGate.IsProduction(env));
        Assert.False(TestAuthenticationEnvironmentGate.IsAllowed(env));
    }

    [Theory]
    [InlineData("Development", "true", true)]
    [InlineData("Test", "True", true)]
    [InlineData("Development", "false", false)]
    [InlineData("Development", null, false)]
    [InlineData("Development", "not-a-bool", false)]
    [InlineData("Production", "true", false)]
    [InlineData("Prod", "true", false)]
    public void IsEnabledForTenant_RequiresNonProductionAndTenantFlag(
        string environmentName,
        string? enabled,
        bool expected)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environmentName);

        Assert.Equal(expected, TestAuthenticationEnvironmentGate.IsEnabledForTenant(env, CreateTenant(enabled)));
    }

    [Fact]
    public void IsEnabledForTenant_ReturnsFalse_WhenNoTenant()
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Development");

        Assert.False(TestAuthenticationEnvironmentGate.IsEnabledForTenant(env, null));
    }

    private static TenantConfiguration CreateTenant(string? testAuthEnabled)
    {
        var settings = new Dictionary<string, string?>();
        if (testAuthEnabled is not null)
            settings["TestAuthentication:Enabled"] = testAuthEnabled;

        return new TenantConfiguration(
            Guid.NewGuid(),
            "TestTenant",
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            []);
    }
}
