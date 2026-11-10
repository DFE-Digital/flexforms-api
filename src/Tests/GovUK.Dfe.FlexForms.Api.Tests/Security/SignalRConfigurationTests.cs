using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Security;

public class SignalRConfigurationTests
{
    [Theory]
    [InlineData("Development", true)]
    [InlineData("Local", true)]
    [InlineData("CodeGeneration", true)]
    [InlineData("Production.CodeGeneration", true)]
    [InlineData("Test", false)]
    [InlineData("Production", false)]
    public void InProcessSignalR_IsOnlyAllowedWhereOneProcessIsTheWholeDeployment(string environmentName, bool expected)
    {
        Assert.Equal(expected, Program.AllowsInProcessSignalR(Config(), Environment(environmentName)));
    }

    [Theory]
    [InlineData("Production", "true", true)]
    [InlineData("Development", "false", false)]
    public void AllowInProcessSetting_OverridesTheEnvironmentDefault(string environmentName, string setting, bool expected)
    {
        var config = Config(new Dictionary<string, string?> { ["SignalR:AllowInProcess"] = setting });

        Assert.Equal(expected, Program.AllowsInProcessSignalR(config, Environment(environmentName)));
    }

    private static IConfiguration Config(Dictionary<string, string?>? values = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(values ?? []).Build();

    private static IWebHostEnvironment Environment(string name)
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns(name);
        return environment;
    }
}
