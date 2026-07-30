using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using GovUK.Dfe.FlexForms.Api.Client.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Api.Client.Security;

/// <summary>
/// Implementation of cache manager using distributed cache and HTTP context
/// Ensures atomic cache operations and consistency.
/// Uses tenant prefix from ApiClientSettings.TenantId for cache key isolation.
/// </summary>
[ExcludeFromCodeCoverage]
public class DistributedCacheManager(
    IDistributedCache distributedCache,
    IHttpContextAccessor httpContextAccessor,
    IInternalUserTokenStore tokenStore,
    IApiClientSettingsProvider settingsProvider,
    ILogger<DistributedCacheManager> logger) : ICacheManager
{
    /// <summary>
    /// Gets a tenant-prefixed cache key if TenantId is configured.
    /// Format: t:{tenantId}:{key}
    /// </summary>
    private string GetTenantPrefixedKey(string key)
    {
        var tenantId = settingsProvider.GetSettings().TenantId;
        if (tenantId.HasValue)
        {
            return $"t:{tenantId}:{key}";
        }
        return key;
    }

    public async Task ClearAllTokenCachesAsync(string userId)
    {
        try
        {
            // Clear OBO token from store
            tokenStore.ClearToken();

            // Clear distributed cache entries
            var cacheKeys = new[]
            {
                GetTenantPrefixedKey($"obo_token_{userId}"),
                GetTenantPrefixedKey($"token_expiry_{userId}"),
                GetTenantPrefixedKey($"user_tokens_{userId}")
            };

            foreach (var key in cacheKeys)
            {
                await distributedCache.RemoveAsync(key);
            }

            // Clear request-scoped cache
            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext != null)
            {
                var itemsToRemove = httpContext.Items.Keys
                    .Where(k => k.ToString()?.Contains("token", StringComparison.OrdinalIgnoreCase) == true)
                    .ToList();

                foreach (var item in itemsToRemove)
                {
                    httpContext.Items.Remove(item);
                }
            }
        }
        catch (Exception ex)
        {
            throw;
        }
    }

    public async Task SetLogoutFlagAsync(string userId, TimeSpan duration)
    {
        try
        {
            // Must use the same tenant-prefixed key as IsLogoutFlagSetAsync / ClearLogoutFlagAsync.
            var key = GetTenantPrefixedKey($"logout_forced_{userId}");
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = duration
            };

            await distributedCache.SetStringAsync(key, "true", options);
            
            // Also set in request scope for immediate effect
            SetRequestScopedFlag("RequireLogout", true);
        }
        catch (Exception ex)
        {
            throw;
        }
    }

    public async Task<bool> IsLogoutFlagSetAsync(string userId)
    {
        try
        {
            // Check request scope first
            if (HasRequestScopedFlag("RequireLogout"))
            {
                return true;
            }

            // Check distributed cache
            var key = GetTenantPrefixedKey($"logout_forced_{userId}");
            var value = await distributedCache.GetStringAsync(key);
            var isSet = !string.IsNullOrEmpty(value);
            
            return isSet;
        }
        catch (Exception ex)
        {
            return false;
        }
    }

    public async Task ClearLogoutFlagAsync(string userId)
    {
        try
        {
            var key = GetTenantPrefixedKey($"logout_forced_{userId}");
            await distributedCache.RemoveAsync(key);
            
            // Clear request scope as well
            var httpContext = httpContextAccessor.HttpContext;
            httpContext?.Items.Remove("RequireLogout");
        }
        catch (Exception ex)
        {
            throw;
        }
    }

    public void SetRequestScopedFlag(string key, object value)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            httpContext.Items[key] = value;
        }
    }

    public T? GetRequestScopedFlag<T>(string key)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext?.Items.TryGetValue(key, out var value) == true && value is T typedValue)
        {
            return typedValue;
        }
        
        return default;
    }

    public bool HasRequestScopedFlag(string key)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var exists = httpContext?.Items.ContainsKey(key) == true;
        return exists;
    }

    public async Task<DateTime?> GetLastActivityAsync(string userId)
    {
        try
        {
            var key = GetTenantPrefixedKey($"last_activity_{userId}");
            var value = await distributedCache.GetStringAsync(key);
            if (!string.IsNullOrEmpty(value))
            {
                // Prefer DateTimeOffset with round-trip kind, then fall back to UTC assumptions
                if (System.DateTimeOffset.TryParseExact(
                        value,
                        "o",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind,
                        out var dto))
                {
                    return dto.UtcDateTime;
                }

                if (System.DateTime.TryParse(
                        value,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                        out var parsed))
                {
                    return parsed;
                }
            }
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task SetLastActivityAsync(string userId, DateTime timestamp, TimeSpan? ttl = null)
    {
        try
        {
            var key = GetTenantPrefixedKey($"last_activity_{userId}");
            var options = new DistributedCacheEntryOptions();
            if (ttl.HasValue)
            {
                options.SlidingExpiration = ttl;
            }
            // Always persist as UTC in round-trip format to avoid DST/local issues
            var utc = timestamp.Kind == DateTimeKind.Utc ? timestamp : timestamp.ToUniversalTime();
            await distributedCache.SetStringAsync(key, utc.ToString("o"), options);
        }
        catch (Exception)
        {
            // swallow; last-activity is best-effort
        }
    }
}
