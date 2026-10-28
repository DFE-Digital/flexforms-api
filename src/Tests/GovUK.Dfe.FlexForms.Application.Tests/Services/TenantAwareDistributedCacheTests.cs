using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class TenantAwareDistributedCacheTests
{
    private readonly IDistributedCache _innerCache;
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly ILogger<TenantAwareDistributedCache> _logger;
    private readonly TenantAwareDistributedCache _cache;

    public TenantAwareDistributedCacheTests()
    {
        _innerCache = Substitute.For<IDistributedCache>();
        _tenantContextAccessor = Substitute.For<ITenantContextAccessor>();
        _logger = Substitute.For<ILogger<TenantAwareDistributedCache>>();
        _cache = new TenantAwareDistributedCache(_innerCache, _tenantContextAccessor, _logger);
    }

    private TenantConfiguration CreateTenant(Guid? id = null)
    {
        return new TenantConfiguration(
            id ?? Guid.NewGuid(), "TestTenant",
            new ConfigurationBuilder().Build(),
            Array.Empty<string>());
    }

    [Fact]
    public void GetTenantPrefixedKey_ShouldPrefixWithTenantId_WhenTenantAvailable()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        _tenantContextAccessor.CurrentTenant.Returns(CreateTenant(tenantId));

        // Act
        var result = _cache.GetTenantPrefixedKey("myKey");

        // Assert
        Assert.Equal($"t:{tenantId}:myKey", result);
    }

    [Fact]
    public void GetTenantPrefixedKey_ShouldThrow_WhenNoTenantContext()
    {
        _tenantContextAccessor.CurrentTenant.Returns((TenantConfiguration?)null);

        Assert.Throws<InvalidOperationException>(() => _cache.GetTenantPrefixedKey("myKey"));
    }

    [Fact]
    public void Get_ShouldCallInnerCache_WithPrefixedKey()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        _tenantContextAccessor.CurrentTenant.Returns(CreateTenant(tenantId));
        var expectedKey = $"t:{tenantId}:myKey";

        // Act
        _cache.Get("myKey");

        // Assert
        _innerCache.Received(1).Get(expectedKey);
    }

    [Fact]
    public async Task GetAsync_ShouldCallInnerCache_WithPrefixedKey()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        _tenantContextAccessor.CurrentTenant.Returns(CreateTenant(tenantId));
        var expectedKey = $"t:{tenantId}:myKey";

        // Act
        await _cache.GetAsync("myKey");

        // Assert
        await _innerCache.Received(1).GetAsync(expectedKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Set_ShouldCallInnerCache_WithPrefixedKey()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        _tenantContextAccessor.CurrentTenant.Returns(CreateTenant(tenantId));
        var expectedKey = $"t:{tenantId}:myKey";
        var value = new byte[] { 1, 2, 3 };
        var options = new DistributedCacheEntryOptions();

        // Act
        _cache.Set("myKey", value, options);

        // Assert
        _innerCache.Received(1).Set(expectedKey, value, options);
    }

    [Fact]
    public async Task SetAsync_ShouldCallInnerCache_WithPrefixedKey()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        _tenantContextAccessor.CurrentTenant.Returns(CreateTenant(tenantId));
        var expectedKey = $"t:{tenantId}:myKey";
        var value = new byte[] { 1, 2, 3 };
        var options = new DistributedCacheEntryOptions();

        // Act
        await _cache.SetAsync("myKey", value, options);

        // Assert
        await _innerCache.Received(1).SetAsync(expectedKey, value, options, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Remove_ShouldCallInnerCache_WithPrefixedKey()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        _tenantContextAccessor.CurrentTenant.Returns(CreateTenant(tenantId));
        var expectedKey = $"t:{tenantId}:myKey";

        // Act
        _cache.Remove("myKey");

        // Assert
        _innerCache.Received(1).Remove(expectedKey);
    }

    [Fact]
    public async Task RemoveAsync_ShouldCallInnerCache_WithPrefixedKey()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        _tenantContextAccessor.CurrentTenant.Returns(CreateTenant(tenantId));
        var expectedKey = $"t:{tenantId}:myKey";

        // Act
        await _cache.RemoveAsync("myKey");

        // Assert
        await _innerCache.Received(1).RemoveAsync(expectedKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Refresh_ShouldCallInnerCache_WithPrefixedKey()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        _tenantContextAccessor.CurrentTenant.Returns(CreateTenant(tenantId));
        var expectedKey = $"t:{tenantId}:myKey";

        // Act
        _cache.Refresh("myKey");

        // Assert
        _innerCache.Received(1).Refresh(expectedKey);
    }

    [Fact]
    public async Task RefreshAsync_ShouldCallInnerCache_WithPrefixedKey()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        _tenantContextAccessor.CurrentTenant.Returns(CreateTenant(tenantId));
        var expectedKey = $"t:{tenantId}:myKey";

        // Act
        await _cache.RefreshAsync("myKey");

        // Assert
        await _innerCache.Received(1).RefreshAsync(expectedKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DifferentTenants_ShouldProduceDifferentPrefixedKeys()
    {
        // Arrange
        var tenant1Id = Guid.NewGuid();
        var tenant2Id = Guid.NewGuid();

        _tenantContextAccessor.CurrentTenant.Returns(CreateTenant(tenant1Id));
        var key1 = _cache.GetTenantPrefixedKey("sameKey");

        _tenantContextAccessor.CurrentTenant.Returns(CreateTenant(tenant2Id));
        var key2 = _cache.GetTenantPrefixedKey("sameKey");

        // Assert
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenInnerCacheIsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TenantAwareDistributedCache(null!, _tenantContextAccessor, _logger));
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenAccessorIsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TenantAwareDistributedCache(_innerCache, null!, _logger));
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TenantAwareDistributedCache(_innerCache, _tenantContextAccessor, null!));
    }
}
