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
        // Local ignores UseAzure unless UseStorageSas opts into Azure with a SAS blob URL.
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(
            useAzure: true,
            useStorageSas: false,
            blobUri: "https://example.blob.core.windows.net/keys/k.xml",
            keyVaultKeyId: "https://example.vault.azure.net/keys/k");
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
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(useAzure: false, useStorageSas: false, blobUri: "", keyVaultKeyId: "");
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
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(
            useAzure: true,
            useStorageSas: false,
            blobUri: "",
            keyVaultKeyId: "https://example.vault.azure.net/keys/k");
        var environment = new TestHostEnvironment("Test");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddTenantSettingsDataProtection(configuration, environment));

        Assert.Contains("BlobUri", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddTenantSettingsDataProtection_UseAzureTrueMissingBlobUri_Throws()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(
            useAzure: true,
            useStorageSas: false,
            blobUri: "",
            keyVaultKeyId: "https://example.vault.azure.net/keys/k");
        var environment = new TestHostEnvironment("Production");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddTenantSettingsDataProtection(configuration, environment));

        Assert.Contains("BlobUri", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddTenantSettingsDataProtection_UseAzureFalseInProduction_UsesLocalKeys()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(useAzure: false, useStorageSas: false, blobUri: "", keyVaultKeyId: "");
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
            useStorageSas: false,
            blobUri: "https://example.blob.core.windows.net/keys/k.xml",
            keyVaultKeyId: "");
        var environment = new TestHostEnvironment("Production");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddTenantSettingsDataProtection(configuration, environment));

        Assert.Contains("KeyVaultKeyId", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddTenantSettingsDataProtection_UseStorageSasWithoutQueryString_Throws()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(
            useAzure: true,
            useStorageSas: true,
            blobUri: "https://example.blob.core.windows.net/keys/k.xml",
            keyVaultKeyId: "https://example.vault.azure.net/keys/k");
        var environment = new TestHostEnvironment("Production");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddTenantSettingsDataProtection(configuration, environment));

        Assert.Contains("SAS", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddTenantSettingsDataProtection_LocalWithUseStorageSasMissingSas_Throws()
    {
        // Opting into Azure on Local via UseStorageSas must still validate the SAS URL.
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(
            useAzure: true,
            useStorageSas: true,
            blobUri: "https://example.blob.core.windows.net/keys/k.xml",
            keyVaultKeyId: "https://example.vault.azure.net/keys/k");
        var environment = new TestHostEnvironment("Local");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddTenantSettingsDataProtection(configuration, environment));

        Assert.Contains("SAS", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddTenantSettingsDataProtection_LocalKeysPathWithEnvironmentVariable_ExpandsIt()
    {
        var keysPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Environment.SetEnvironmentVariable(KeysPathVariable, keysPath);

        try
        {
            var services = new ServiceCollection();
            var configuration = BuildConfiguration(
                useAzure: false,
                useStorageSas: false,
                blobUri: "",
                keyVaultKeyId: "",
                localKeysPath: $"${{{KeysPathVariable}}}");

            services.AddTenantSettingsDataProtection(configuration, new TestHostEnvironment("Local"));

            using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("TenantSettings.v1")
                .Protect("hello");

            Assert.NotEmpty(Directory.GetFiles(keysPath, "key-*.xml"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(KeysPathVariable, null);
            if (Directory.Exists(keysPath))
                Directory.Delete(keysPath, recursive: true);
        }
    }

    [Fact]
    public void AddTenantSettingsDataProtection_LocalKeysPathForAnotherPlatform_FallsBackToDefaultLocation()
    {
        // "/home/app/..." resolves to "C:\home\app\..." on Windows, which would quietly create a
        // brand new key ring instead of reading the developer's existing one.
        var foreignPath = OperatingSystem.IsWindows()
            ? "/home/app/.aspnet/DataProtection-Keys"
            : @"C:\Users\app\AppData\Local\ASP.NET\DataProtection-Keys";

        var services = new ServiceCollection();
        var configuration = BuildConfiguration(
            useAzure: false,
            useStorageSas: false,
            blobUri: "",
            keyVaultKeyId: "",
            localKeysPath: foreignPath);

        services.AddTenantSettingsDataProtection(configuration, new TestHostEnvironment("Local"));

        using var provider = services.BuildServiceProvider();
        var protector = provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("TenantSettings.v1");

        Assert.Equal("hello", protector.Unprotect(protector.Protect("hello")));
        Assert.False(Directory.Exists(Path.GetFullPath(foreignPath)));
    }

    private const string KeysPathVariable = "FLEXFORMS_TEST_DATA_PROTECTION_KEYS";

    private static IConfiguration BuildConfiguration(
        bool useAzure,
        bool useStorageSas,
        string blobUri,
        string keyVaultKeyId,
        string localKeysPath = "") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:UseAzure"] = useAzure.ToString(),
                ["DataProtection:UseStorageSas"] = useStorageSas.ToString(),
                ["DataProtection:ApplicationName"] = "GovUK.Dfe.FlexForms.Api.Tests",
                ["DataProtection:BlobUri"] = blobUri,
                ["DataProtection:KeyVaultKeyId"] = keyVaultKeyId,
                ["DataProtection:LocalKeysPath"] = localKeysPath
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
