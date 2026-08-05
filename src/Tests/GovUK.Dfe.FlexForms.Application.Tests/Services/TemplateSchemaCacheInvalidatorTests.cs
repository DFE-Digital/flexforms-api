using GovUK.Dfe.CoreLibs.Caching.Helpers;
using GovUK.Dfe.CoreLibs.Caching.Interfaces;
using GovUK.Dfe.FlexForms.Application.Common;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class TemplateSchemaCacheInvalidatorTests
{
    [Fact]
    public async Task InvalidateForTemplateAsync_RemovesTenantScopedSchemaPattern()
    {
        var tenantId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var tenant = new TenantConfiguration(tenantId, "Test", config, Array.Empty<string>());
        var tenantAccessor = Substitute.For<ITenantContextAccessor>();
        tenantAccessor.CurrentTenant.Returns(tenant);
        var redis = Substitute.For<IAdvancedRedisCacheService>();

        var invalidator = new TemplateSchemaCacheInvalidator(redis, tenantAccessor);

        await invalidator.InvalidateForTemplateAsync(templateId);

        var expectedPattern = TenantCacheKeyHelper.CreateTenantScopedKey(
            tenantAccessor,
            $"TemplateSchema_PrincipalId_{CacheKeyHelper.GenerateHashedCacheKey(templateId.ToString())}_*");

        await redis.Received(1).RemoveByPatternAsync(expectedPattern);
    }

    [Fact]
    public async Task InvalidateForTemplateAsync_IgnoresEmptyTemplateId()
    {
        var redis = Substitute.For<IAdvancedRedisCacheService>();
        var invalidator = new TemplateSchemaCacheInvalidator(
            redis,
            Substitute.For<ITenantContextAccessor>());

        await invalidator.InvalidateForTemplateAsync(Guid.Empty);

        await redis.DidNotReceive().RemoveByPatternAsync(Arg.Any<string>());
    }
}
