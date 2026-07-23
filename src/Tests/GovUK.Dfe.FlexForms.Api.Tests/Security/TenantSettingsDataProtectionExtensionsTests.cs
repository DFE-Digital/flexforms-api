using GovUK.Dfe.FlexForms.Api.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Security;

public class TenantSettingsDataProtectionExtensionsTests
{
    [Theory]
    [InlineData("Local")]
    public void AddTenantSettingsDataProtection_Local_UsesLocalKeysEvenWhenUseAzureTrue(string environmentName)
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(useAzure: true, blobUri: "https://example.blob.core.windows.net/keys/k.xml", keyVaultKeyId: "https://example.vault.azure.net/keys/k");
        var environment = new TestHostEnvironment(environmentName);

        var builder = services.AddTenantSettingsDataProtection(configuration, environment);

        Assert.NotNull(builder);
        using var provider = services.BuildServiceProvider();
        var dataProtection = provider.GetRequiredService<IDataProtectionProvider>();
        var protector = dataProtection.CreateProtector("TenantSettings.v1");
        var cipher = protector.Protect("hello");
        Assert.Equal("hello", protector.Unprotect(cipher));
    }

    [Theory]
    [InlineData("Test")]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void AddTenantSettingsDataProtection_NonLocalWithUseAzureFalse_UsesLocalKeys(string environmentName)
    {
        // Deployed environments (including the Azure "Test" environment) and integration test hosts
        // that set UseAzure=false must use the local key ring by flag, not by environment name.
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(useAzure: false, blobUri: "", keyVaultKeyId: "");
        var environment = new TestHostEnvironment(environmentName);

        services.AddTenantSettingsDataProtection(configuration, environment);

        using var provider = services.BuildServiceProvider();
        var dataProtection = provider.GetRequiredService<IDataProtectionProvider>();
        var protector = dataProtection.CreateProtector("TenantSettings.v1");
        var cipher = protector.Protect("hello");
        Assert.Equal("hello", protector.Unprotect(cipher));
    }

    [Fact]
    public void AddTenantSettingsDataProtection_TestEnvironmentWithUseAzureTrue_RequiresAzureConfig()
    {
        // Guards the Azure "Test" environment: UseAzure=true must NOT fall back to local keys,
        // so missing Azure config must throw rather than silently using local protection.
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(useAzure: true, blobUri: "", keyVaultKeyId: "https://example.vault.azure.net/keys/k");
        var environment = new TestHostEnvironment("Test");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddTenantSettingsDataProtection(configuration, environment));

        Assert.Contains("BlobUri", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddTenantSettingsDataProtection_UseAzureTrueMissingBlobUri_Throws()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(useAzure: true, blobUri: "", keyVaultKeyId: "https://example.vault.azure.net/keys/k");
        var environment = new TestHostEnvironment("Production");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddTenantSettingsDataProtection(configuration, environment));

        Assert.Contains("BlobUri", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddTenantSettingsDataProtection_UseAzureFalseInProduction_UsesLocalKeys()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(useAzure: false, blobUri: "", keyVaultKeyId: "");
        var environment = new TestHostEnvironment("Production");

        services.AddTenantSettingsDataProtection(configuration, environment);
        using var provider = services.BuildServiceProvider();
        var dataProtection = provider.GetRequiredService<IDataProtectionProvider>();
        Assert.NotNull(dataProtection.CreateProtector("TenantSettings.v1"));
    }

    [Fact]
    public void AddTenantSettingsDataProtection_UseAzureTrueMissingKeyVaultKeyId_Throws()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(
            useAzure: true,
            blobUri: "https://example.blob.core.windows.net/keys/k.xml",
            keyVaultKeyId: "");
        var environment = new TestHostEnvironment("Production");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddTenantSettingsDataProtection(configuration, environment));

        Assert.Contains("KeyVaultKeyId", ex.Message, StringComparison.Ordinal);
    }

    private static IConfiguration BuildConfiguration(bool useAzure, string blobUri, string keyVaultKeyId) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:UseAzure"] = useAzure.ToString(),
                ["DataProtection:ApplicationName"] = "GovUK.Dfe.FlexForms.Api.Tests",
                ["DataProtection:BlobUri"] = blobUri,
                ["DataProtection:KeyVaultKeyId"] = keyVaultKeyId
            })
            .Build();

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
