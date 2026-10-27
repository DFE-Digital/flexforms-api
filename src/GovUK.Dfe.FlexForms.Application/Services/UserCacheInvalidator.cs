using GovUK.Dfe.CoreLibs.Caching.Helpers;
using GovUK.Dfe.CoreLibs.Caching.Interfaces;
using GovUK.Dfe.FlexForms.Application.Common;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Utils.Diagnostics;

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
            var normalizedEmail = email.Trim().ToLowerInvariant();
            var userClaimsKey = TenantCacheKeyHelper.CreateTenantScopedKey(
                tenantContextAccessor,
                $"UserClaims_{CacheKeyHelper.GenerateHashedCacheKey(normalizedEmail)}");
            cacheService.Remove(userClaimsKey);

            var emailListingPattern = TenantCacheKeyHelper.CreateTenantScopedKey(
                tenantContextAccessor,
                $"Applications_ForUser_{CacheKeyHelper.GenerateHashedCacheKey(normalizedEmail)}*");
            await RemovePatternAsync(emailListingPattern);

            // Drop cached OBO JWTs so the next API call re-exchanges with the current role.
            await InvalidateInternalTokensAsync(email.Trim());
            if (!string.Equals(email.Trim(), normalizedEmail, StringComparison.Ordinal))
                await InvalidateInternalTokensAsync(normalizedEmail);
        }

        var userIdHash = CacheKeyHelper.GenerateHashedCacheKey(userId.Value.ToString());

        cacheService.Remove(TenantCacheKeyHelper.CreateTenantScopedKey(
            tenantContextAccessor,
            $"Permissions_All_UserId_{userIdHash}"));

        cacheService.Remove(TenantCacheKeyHelper.CreateTenantScopedKey(
            tenantContextAccessor,
            $"Template_Permissions_ByUiD_{userIdHash}"));

        // Admin/template listings are principal-scoped; clear tenant template lists so create/update
        // and permission changes are visible immediately.
        await RemovePatternAsync(
            TenantCacheKeyHelper.CreateTenantScopedKey(
                tenantContextAccessor,
                "Applications_ByTemplate_*"));

        if (!string.IsNullOrWhiteSpace(externalProviderId))
        {
            var externalIdListingPattern = TenantCacheKeyHelper.CreateTenantScopedKey(
                tenantContextAccessor,
                $"Applications_ForUserExternal_{CacheKeyHelper.GenerateHashedCacheKey(externalProviderId.Trim())}*");
            await RemovePatternAsync(externalIdListingPattern);

            await InvalidateInternalTokensAsync(externalProviderId.Trim());
        }
    }

    /// <inheritdoc />
    public async Task InvalidateApplicationListingsForUserAsync(
        string? email,
        string? externalProviderId,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            var normalizedEmail = email.Trim().ToLowerInvariant();
            await RemovePatternAsync(TenantCacheKeyHelper.CreateTenantScopedKey(
                tenantContextAccessor,
                $"Applications_ForUser_{CacheKeyHelper.GenerateHashedCacheKey(normalizedEmail)}*"));
        }

        await RemovePatternAsync(
            TenantCacheKeyHelper.CreateTenantScopedKey(
                tenantContextAccessor,
                "Applications_ByTemplate_*"));

        if (!string.IsNullOrWhiteSpace(externalProviderId))
        {
            await RemovePatternAsync(TenantCacheKeyHelper.CreateTenantScopedKey(
                tenantContextAccessor,
                $"Applications_ForUserExternal_{CacheKeyHelper.GenerateHashedCacheKey(externalProviderId.Trim())}*"));
        }
    }

    /// <inheritdoc />
    public async Task InvalidateApplicationListingsAsync(CancellationToken cancellationToken = default)
    {
        var patterns = new[]
        {
            "Applications_ByTemplate_*",
            "Applications_ForUser_*",
            "Applications_ForUserExternal_*"
        };

        foreach (var suffix in patterns)
        {
            var pattern = TenantCacheKeyHelper.CreateTenantScopedKey(tenantContextAccessor, suffix);
            await RemovePatternAsync(pattern);
        }
    }

    /// <inheritdoc />
    public async Task InvalidateTenantUserClaimsAsync(CancellationToken cancellationToken = default)
    {
        // Web loads permissions via GetMyPermissions (Permissions_All_UserId_*), not UserClaims_* alone.
        // Clear every tenant-scoped permission cache so role changes apply on the next request.
        var patterns = new[]
        {
            "UserClaims_*",
            "Permissions_All_UserId_*",
            "Template_Permissions_ByUiD_*"
        };

        foreach (var suffix in patterns)
        {
            var pattern = TenantCacheKeyHelper.CreateTenantScopedKey(tenantContextAccessor, suffix);
            await RemovePatternAsync(pattern);
        }
    }

    private async Task RemovePatternAsync(string pattern)
    {
        using (RequestTimings.Measure("redis pattern removal"))
            await advancedRedisCacheService.RemoveByPatternAsync(pattern);
    }

    private async Task RemoveKeyAsync(string key)
    {
        using (RequestTimings.Measure("redis key removal"))
            await advancedRedisCacheService.RemoveAsync(key);
    }

    private async Task InvalidateInternalTokensAsync(string userKey)
    {
        var tenantId = tenantContextAccessor.CurrentTenant?.Id;
        var exactKey = tenantId.HasValue
            ? $"t:{tenantId}:{InternalTokenKeyPrefix}{userKey}"
            : $"{InternalTokenKeyPrefix}{userKey}";

        await RemoveKeyAsync(exactKey);

        // Also clear case variants and any legacy keys that embedded this identity.
        await RemovePatternAsync($"*{InternalTokenKeyPrefix}*{userKey}*");

        if (!string.Equals(userKey, userKey.ToLowerInvariant(), StringComparison.Ordinal))
        {
            var lowerKey = tenantId.HasValue
                ? $"t:{tenantId}:{InternalTokenKeyPrefix}{userKey.ToLowerInvariant()}"
                : $"{InternalTokenKeyPrefix}{userKey.ToLowerInvariant()}";
            await RemoveKeyAsync(lowerKey);
        }
    }
}
