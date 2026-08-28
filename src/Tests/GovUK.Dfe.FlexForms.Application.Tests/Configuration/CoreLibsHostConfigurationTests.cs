using GovUK.Dfe.FlexForms.Utils.Caching;
using GovUK.Dfe.FlexForms.Utils.Configuration;
using Microsoft.Extensions.Configuration;

namespace GovUK.Dfe.FlexForms.Application.Tests.Configuration;

public class CoreLibsHostConfigurationTests
{
    [Fact]
    public void Resolve_ShouldPreferGlobalConfiguration_WhenFileStorageProviderPresent()
    {
        var root = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Test",
                ["GlobalConfiguration:FileStorage:Provider"] = "Local",
                ["GlobalConfiguration:FileStorage:Local:BaseDirectory"] = "/global-uploads",
                ["ConnectionStrings:Redis"] = "localhost:6379"
            })
            .Build();

        var firstTenant = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:Provider"] = "Local",
                ["FileStorage:Local:BaseDirectory"] = "/tenant-uploads"
            })
            .Build();

        var resolved = CoreLibsHostConfiguration.Resolve(root, firstTenant);

        Assert.Equal("Local", resolved["FileStorage:Provider"]);
        Assert.Equal("/global-uploads", resolved["FileStorage:Local:BaseDirectory"]);
        Assert.Equal(FlexFormsCacheKeys.RedisKeyPrefix, resolved["CacheSettings:Redis:KeyPrefix"]);
        Assert.Equal("localhost:6379", resolved.GetConnectionString("Redis"));
    }

    [Fact]
    public void Resolve_ShouldFillRedisFromFirstTenant_WhenGlobalHasFileStorageButNoRedis()
    {
        var root = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "CodeGeneration",
                ["GlobalConfiguration:FileStorage:Provider"] = "Local",
                ["GlobalConfiguration:FileStorage:Local:BaseDirectory"] = "/global-uploads"
            })
            .Build();

        var firstTenant = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = "tenant-redis:6379"
            })
            .Build();

        var resolved = CoreLibsHostConfiguration.Resolve(root, firstTenant);

        Assert.Equal("/global-uploads", resolved["FileStorage:Local:BaseDirectory"]);
        Assert.Equal("tenant-redis:6379", resolved.GetConnectionString("Redis"));
        Assert.Equal("tenant-redis:6379", resolved["NotificationService:RedisConnectionString"]);
    }

    [Fact]
    public void Resolve_ShouldFallBackToFirstTenant_InLocalWhenGlobalMissing()
    {
        var root = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Local"
            })
            .Build();

        var firstTenant = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:Provider"] = "Local",
                ["FileStorage:Local:BaseDirectory"] = "/tenant-uploads"
            })
            .Build();

        var resolved = CoreLibsHostConfiguration.Resolve(root, firstTenant);

        Assert.Equal("/tenant-uploads", resolved["FileStorage:Local:BaseDirectory"]);
    }

    [Fact]
    public void Resolve_ShouldThrow_InNonLocalWhenGlobalFileStorageMissing()
    {
        var root = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Test"
            })
            .Build();

        var firstTenant = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:Provider"] = "Local",
                ["FileStorage:Local:BaseDirectory"] = "/tenant-uploads"
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(
            () => CoreLibsHostConfiguration.Resolve(root, firstTenant));

        Assert.Contains("GlobalConfiguration:FileStorage:Provider", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_ShouldAllowExplicitFallbackFlag_OutsideLocal()
    {
        var root = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Test",
                ["AllowTenantHostConfigFallback"] = "true"
            })
            .Build();

        var firstTenant = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:Provider"] = "Azure",
                ["FileStorage:Azure:ShareName"] = "uploads"
            })
            .Build();

        var resolved = CoreLibsHostConfiguration.Resolve(root, firstTenant);

        Assert.Equal("Azure", resolved["FileStorage:Provider"]);
        Assert.Equal("uploads", resolved["FileStorage:Azure:ShareName"]);
    }

    [Fact]
    public void AllowTenantFallback_ShouldRespectExplicitFalse_EvenInLocal()
    {
        var root = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Local",
                ["AllowTenantHostConfigFallback"] = "false"
            })
            .Build();

        Assert.False(CoreLibsHostConfiguration.AllowTenantFallback(root));
    }
}
