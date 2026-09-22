using GovUK.Dfe.FlexForms.Application.Security;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GovUK.Dfe.FlexForms.Application.Tests.Security;

public class TenantSettingSecretPlaintextGateTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Local")]
    [InlineData("Dev")]
    [InlineData("Test")]
    [InlineData("Testing")]
    [InlineData("development")]
    public void IsPlaintextEnvironment_ShouldBeTrue_ForDevAndTest(string environmentName)
    {
        Assert.True(TenantSettingSecretPlaintextGate.IsPlaintextEnvironment(environmentName));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Prod")]
    [InlineData("Staging")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Unknown")]
    public void IsPlaintextEnvironment_ShouldBeFalse_ForProductionAndUnknown(string? environmentName)
    {
        Assert.False(TenantSettingSecretPlaintextGate.IsPlaintextEnvironment(environmentName));
    }

    [Fact]
    public void AllowsSuperAdminPlaintext_ShouldRequireSuperAdminAndDevTest()
    {
        Assert.True(TenantSettingSecretPlaintextGate.AllowsSuperAdminPlaintext(
            new FakeHostEnvironment("Test"), isInteractiveSuperAdmin: true));
        Assert.False(TenantSettingSecretPlaintextGate.AllowsSuperAdminPlaintext(
            new FakeHostEnvironment("Test"), isInteractiveSuperAdmin: false));
        Assert.False(TenantSettingSecretPlaintextGate.AllowsSuperAdminPlaintext(
            new FakeHostEnvironment("Production"), isInteractiveSuperAdmin: true));
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
