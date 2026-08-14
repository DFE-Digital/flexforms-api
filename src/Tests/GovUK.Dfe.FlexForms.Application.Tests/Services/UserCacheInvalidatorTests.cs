using GovUK.Dfe.CoreLibs.Caching.Interfaces;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class UserCacheInvalidatorTests
{
    [Fact]
    public async Task InvalidateForUserAsync_ShouldRemovePermissionListingAndInternalTokenKeys()
    {
        var cacheService = Substitute.For<ICacheService<IRedisCacheType>>();
        var advancedRedisCacheService = Substitute.For<IAdvancedRedisCacheService>();
        var tenantContextAccessor = Substitute.For<ITenantContextAccessor>();
        tenantContextAccessor.CurrentTenant.Returns(
            new TenantConfiguration(Guid.NewGuid(), "TestTenant", new ConfigurationBuilder().Build(), []));

        var email = "Contributor@Example.com";
        var userId = new UserId(Guid.NewGuid());
        var invalidator = new UserCacheInvalidator(cacheService, advancedRedisCacheService, tenantContextAccessor);

        await invalidator.InvalidateForUserAsync(email, "external-id", userId);

        cacheService.Received(3).Remove(Arg.Any<string>());
        // Email listing + ByTemplate + external listing + internal-token pattern (email) +
        // internal-token pattern (email lower) + internal-token pattern (external id)
        await advancedRedisCacheService.Received(6).RemoveByPatternAsync(Arg.Any<string>());
        await advancedRedisCacheService.Received().RemoveByPatternAsync(Arg.Is<string>(p => p.Contains("Applications_ByTemplate_")));
        await advancedRedisCacheService.Received().RemoveAsync(Arg.Is<string>(k => k.Contains("FlexForms:InternalToken:")));
    }

    [Fact]
    public async Task InvalidateForUserAsync_ShouldRemoveOnlyEmailListingKey_WhenExternalProviderIdMissing()
    {
        var cacheService = Substitute.For<ICacheService<IRedisCacheType>>();
        var advancedRedisCacheService = Substitute.For<IAdvancedRedisCacheService>();
        var tenantContextAccessor = Substitute.For<ITenantContextAccessor>();
        tenantContextAccessor.CurrentTenant.Returns(
            new TenantConfiguration(Guid.NewGuid(), "TestTenant", new ConfigurationBuilder().Build(), []));

        var invalidator = new UserCacheInvalidator(cacheService, advancedRedisCacheService, tenantContextAccessor);

        await invalidator.InvalidateForUserAsync("user@example.com", null, new UserId(Guid.NewGuid()));

        await advancedRedisCacheService.Received(1).RemoveByPatternAsync(Arg.Is<string>(p => p.Contains("Applications_ForUser_")));
        await advancedRedisCacheService.Received(1).RemoveByPatternAsync(Arg.Is<string>(p => p.Contains("Applications_ByTemplate_")));
        await advancedRedisCacheService.Received().RemoveByPatternAsync(Arg.Is<string>(p => p.Contains("FlexForms:InternalToken:")));
    }

    [Fact]
    public async Task InvalidateTenantUserClaimsAsync_ShouldRemoveAllTenantPermissionCachePatterns()
    {
        var cacheService = Substitute.For<ICacheService<IRedisCacheType>>();
        var advancedRedisCacheService = Substitute.For<IAdvancedRedisCacheService>();
        var tenantContextAccessor = Substitute.For<ITenantContextAccessor>();
        tenantContextAccessor.CurrentTenant.Returns(
            new TenantConfiguration(Guid.NewGuid(), "TestTenant", new ConfigurationBuilder().Build(), []));

        var invalidator = new UserCacheInvalidator(cacheService, advancedRedisCacheService, tenantContextAccessor);

        await invalidator.InvalidateTenantUserClaimsAsync();

        await advancedRedisCacheService.Received(1).RemoveByPatternAsync(Arg.Is<string>(p => p.Contains("UserClaims_")));
        await advancedRedisCacheService.Received(1).RemoveByPatternAsync(Arg.Is<string>(p => p.Contains("Permissions_All_UserId_")));
        await advancedRedisCacheService.Received(1).RemoveByPatternAsync(Arg.Is<string>(p => p.Contains("Template_Permissions_ByUiD_")));
    }
}
