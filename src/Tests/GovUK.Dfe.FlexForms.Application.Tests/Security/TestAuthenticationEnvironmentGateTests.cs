using GovUK.Dfe.FlexForms.Application.Security;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Security;

public class TestAuthenticationEnvironmentGateTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("dev")]
    [InlineData("Staging")]
    [InlineData("Test")]
    [InlineData(null)]
    [InlineData("")]
    public void IsAllowed_ReturnsTrue_OutsideProduction(string? environmentName)
    {
        Assert.True(TestAuthenticationEnvironmentGate.IsAllowed(environmentName));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("production")]
    [InlineData("Prod")]
    [InlineData("PROD")]
    public void IsAllowed_ReturnsFalse_InProduction(string environmentName)
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
}
