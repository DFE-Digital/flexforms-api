using GovUK.Dfe.CoreLibs.Caching.Helpers;
using GovUK.Dfe.CoreLibs.Caching.Interfaces;
using GovUK.Dfe.FlexForms.Application.Common;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Removes tenant-scoped Redis cache entries used for user permissions, OBO tokens, and application listings.
/// </summary>
public sealed class UserCacheInvalidator(
    ICacheService<IRedisCacheType> cacheService,
    IAdvancedRedisCacheService advancedRedisCacheService,
    ITenantContextAccessor tenantContextAccessor) : IUserCacheInvalidator
{
    /// <summary>
    /// Must match <c>CachedInternalUserTokenStore</c> cache key prefix (Api.Client).
    /// </summary>
    private const string InternalTokenKeyPrefix = "FlexForms:InternalToken:";

    /// <inheritdoc />
    public async Task InvalidateForUserAsync(
        string? email,
        string? externalProviderId,
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);

        if (!string.IsNullOrWhiteSpace(email))
        {
            var normalizedEmail = email.Trim();
            var userClaimsKey = TenantCacheKeyHelper.CreateTenantScopedKey(
                tenantContextAccessor,
                $"UserClaims_{CacheKeyHelper.GenerateHashedCacheKey(normalizedEmail.ToLowerInvariant())}");
            cacheService.Remove(userClaimsKey);

            var emailListingPattern = TenantCacheKeyHelper.CreateTenantScopedKey(
                tenantContextAccessor,
                $"Applications_ForUser_{CacheKeyHelper.GenerateHashedCacheKey(normalizedEmail)}*");
            await advancedRedisCacheService.RemoveByPatternAsync(emailListingPattern);

            // Drop cached OBO JWTs so the next API call re-exchanges with the current role.
            await InvalidateInternalTokensAsync(normalizedEmail);
        }

        var userIdHash = CacheKeyHelper.GenerateHashedCacheKey(userId.Value.ToString());

        cacheService.Remove(TenantCacheKeyHelper.CreateTenantScopedKey(
            tenantContextAccessor,
            $"Permissions_All_UserId_{userIdHash}"));

        cacheService.Remove(TenantCacheKeyHelper.CreateTenantScopedKey(
            tenantContextAccessor,
            $"Template_Permissions_ByUiD_{userIdHash}"));

        if (!string.IsNullOrWhiteSpace(externalProviderId))
        {
            var externalIdListingPattern = TenantCacheKeyHelper.CreateTenantScopedKey(
                tenantContextAccessor,
                $"Applications_ForUserExternal_{CacheKeyHelper.GenerateHashedCacheKey(externalProviderId)}*");
            await advancedRedisCacheService.RemoveByPatternAsync(externalIdListingPattern);

            await InvalidateInternalTokensAsync(externalProviderId.Trim());
        }
    }

    /// <inheritdoc />
    public async Task InvalidateTenantUserClaimsAsync(CancellationToken cancellationToken = default)
    {
        var pattern = TenantCacheKeyHelper.CreateTenantScopedKey(
            tenantContextAccessor,
            "UserClaims_*");
        await advancedRedisCacheService.RemoveByPatternAsync(pattern);
    }

    private async Task InvalidateInternalTokensAsync(string userKey)
    {
        var tenantId = tenantContextAccessor.CurrentTenant?.Id;
        var exactKey = tenantId.HasValue
            ? $"t:{tenantId}:{InternalTokenKeyPrefix}{userKey}"
            : $"{InternalTokenKeyPrefix}{userKey}";

        await advancedRedisCacheService.RemoveAsync(exactKey);

        // Also clear case variants and any legacy keys that embedded this identity.
        await advancedRedisCacheService.RemoveByPatternAsync($"*{InternalTokenKeyPrefix}*{userKey}*");

        if (!string.Equals(userKey, userKey.ToLowerInvariant(), StringComparison.Ordinal))
        {
            var lowerKey = tenantId.HasValue
                ? $"t:{tenantId}:{InternalTokenKeyPrefix}{userKey.ToLowerInvariant()}"
                : $"{InternalTokenKeyPrefix}{userKey.ToLowerInvariant()}";
            await advancedRedisCacheService.RemoveAsync(lowerKey);
        }
    }
}
