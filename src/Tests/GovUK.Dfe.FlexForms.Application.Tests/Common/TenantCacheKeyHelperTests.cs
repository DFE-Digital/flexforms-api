using GovUK.Dfe.FlexForms.Application.Common;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Common;

public class TenantCacheKeyHelperTests
{
    [Fact]
    public void CreateTenantScopedKey_ShouldPrefixWithTenantId_WhenTenantContextAvailable()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var tenantConfig = new TenantConfiguration(
            tenantId, "TestTenant",
            new ConfigurationBuilder().Build(),
            Array.Empty<string>());
        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.CurrentTenant.Returns(tenantConfig);

        // Act
        var result = TenantCacheKeyHelper.CreateTenantScopedKey(accessor, "MyKey");

        // Assert
        Assert.Equal($"t:{tenantId}:MyKey", result);
    }

    [Fact]
    public void CreateTenantScopedKey_ShouldThrow_WhenNoTenantContext()
    {
        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.CurrentTenant.Returns((TenantConfiguration?)null);

        Assert.Throws<InvalidOperationException>(
            () => TenantCacheKeyHelper.CreateTenantScopedKey(accessor, "MyKey"));
    }

    [Fact]
    public void CreateTenantScopedKey_ShouldThrow_WhenAccessorIsNull()
    {
        Assert.Throws<InvalidOperationException>(
            () => TenantCacheKeyHelper.CreateTenantScopedKey(null, "MyKey"));
    }

    [Fact]
    public void CreateTenantScopedKey_ShouldNotDoublePrefix_WhenKeyAlreadyTenantScoped()
    {
        var tenantId = Guid.NewGuid();
        var tenantConfig = new TenantConfiguration(
            tenantId, "TestTenant",
            new ConfigurationBuilder().Build(),
            Array.Empty<string>());
        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.CurrentTenant.Returns(tenantConfig);
        var existing = $"t:{tenantId}:MyKey";

        var result = TenantCacheKeyHelper.CreateTenantScopedKey(accessor, existing);

        Assert.Equal(existing, result);
    }

    [Fact]
    public void CreateTenantScopedKey_ShouldProduceDifferentKeys_ForDifferentTenants()
    {
        // Arrange
        var tenant1Id = Guid.NewGuid();
        var tenant2Id = Guid.NewGuid();

        var tenant1 = new TenantConfiguration(tenant1Id, "Tenant1", new ConfigurationBuilder().Build(), Array.Empty<string>());
        var tenant2 = new TenantConfiguration(tenant2Id, "Tenant2", new ConfigurationBuilder().Build(), Array.Empty<string>());

        var accessor1 = Substitute.For<ITenantContextAccessor>();
        accessor1.CurrentTenant.Returns(tenant1);

        var accessor2 = Substitute.For<ITenantContextAccessor>();
        accessor2.CurrentTenant.Returns(tenant2);

        // Act
        var key1 = TenantCacheKeyHelper.CreateTenantScopedKey(accessor1, "SameKey");
        var key2 = TenantCacheKeyHelper.CreateTenantScopedKey(accessor2, "SameKey");

        // Assert
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void CreateTenantScopedKey_ShouldPreserveBaseKey_InResult()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var tenantConfig = new TenantConfiguration(tenantId, "Test", new ConfigurationBuilder().Build(), Array.Empty<string>());
        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.CurrentTenant.Returns(tenantConfig);
        var baseKey = "Applications_ForUser_abc123";

        // Act
        var result = TenantCacheKeyHelper.CreateTenantScopedKey(accessor, baseKey);

        // Assert
        Assert.EndsWith(baseKey, result);
        Assert.StartsWith("t:", result);
    }
}
