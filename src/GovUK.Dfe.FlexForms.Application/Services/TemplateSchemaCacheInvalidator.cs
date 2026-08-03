using GovUK.Dfe.CoreLibs.Caching.Helpers;
using GovUK.Dfe.CoreLibs.Caching.Interfaces;
using GovUK.Dfe.FlexForms.Application.Common;
using GovUK.Dfe.FlexForms.Domain.Tenancy;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Clears cached latest-template-schema payloads after a new version is published.
/// </summary>
public interface ITemplateSchemaCacheInvalidator
{
    /// <summary>
    /// Removes all principal-scoped latest-schema cache entries for the template in the current tenant.
    /// </summary>
    Task InvalidateForTemplateAsync(Guid templateId, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class TemplateSchemaCacheInvalidator(
    IAdvancedRedisCacheService advancedRedisCacheService,
    ITenantContextAccessor tenantContextAccessor) : ITemplateSchemaCacheInvalidator
{
    /// <inheritdoc />
    public async Task InvalidateForTemplateAsync(Guid templateId, CancellationToken cancellationToken = default)
    {
        if (templateId == Guid.Empty)
            return;

        // Matches GetLatestTemplateSchemaQueryHandler:
        // TemplateSchema_PrincipalId_{hash(templateId)}_{principalId}
        var templateHash = CacheKeyHelper.GenerateHashedCacheKey(templateId.ToString());
        var pattern = TenantCacheKeyHelper.CreateTenantScopedKey(
            tenantContextAccessor,
            $"TemplateSchema_PrincipalId_{templateHash}_*");

        await advancedRedisCacheService.RemoveByPatternAsync(pattern);
    }
}
