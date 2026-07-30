using System;
using System.Diagnostics.CodeAnalysis;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using GovUK.Dfe.FlexForms.Api.Client.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Api.Client.Security;

/// <summary>
/// Caches internal user tokens using the distributed cache with tenant prefix
/// based on the configured TenantId in ApiClientSettings.
/// </summary>
[ExcludeFromCodeCoverage]
public class CachedInternalUserTokenStore(
    IHttpContextAccessor httpContextAccessor,
    IDistributedCache distributedCache,
    IApiClientSettingsProvider settingsProvider,
    ILogger<CachedInternalUserTokenStore> logger)
    : IInternalUserTokenStore
{
    private const string TokenKey = "__InternalUserToken";
    private const string CacheKeyPrefix = "FlexForms:InternalToken:";
    
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
    /// <summary>
    /// Unified expiry buffer - consistent with TokenExpiryService
    /// </summary>
    private static readonly TimeSpan ExpiryBuffer = TimeSpan.FromMinutes(5);

    public string? GetToken()
    {
        var ctx = httpContextAccessor.HttpContext;
        if (ctx == null) 
        {
            return null;
        }

        // First check HttpContext.Items for this request
        if (ctx.Items.TryGetValue(TokenKey, out var tokenObj) && tokenObj is string requestToken)
        {
            if (IsTokenValid(requestToken))
            {
                return requestToken;
            }
            
            ctx.Items.Remove(TokenKey);
        }

        // Then check distributed cache
        var userId = GetUserIdFromContext(ctx);
        if (userId != null)
        {
            var cacheKey = GetTenantPrefixedKey($"{CacheKeyPrefix}{userId}");
            
            var cachedTokenJson = distributedCache.GetString(cacheKey);
            
            if (!string.IsNullOrEmpty(cachedTokenJson))
            {
                try
                {
                    var cachedToken = JsonSerializer.Deserialize<CachedTokenData>(cachedTokenJson);
                    if (cachedToken != null)
                    {
                        if (IsTokenValid(cachedToken.Token))
                        {
                            // Store in HttpContext.Items for subsequent requests in this request
                            ctx.Items[TokenKey] = cachedToken.Token;
                            return cachedToken.Token;
                        }
                        else
                        {
                            // Remove expired token from cache
                            distributedCache.Remove(cacheKey);
                        }
                    }
                    else
                    {
                        distributedCache.Remove(cacheKey);
                    }
                }
                catch (JsonException ex)
                {
                    distributedCache.Remove(cacheKey);
                }
            }
        }

        return null;
    }

    public void SetToken(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return;
        }
        
        var ctx = httpContextAccessor.HttpContext;
        if (ctx == null) 
        {
            return;
        }

        ctx.Items[TokenKey] = token;

        // Store in distributed cache for future requests
        var userId = GetUserIdFromContext(ctx);
        if (userId != null)
        {
            var cacheKey = GetTenantPrefixedKey($"{CacheKeyPrefix}{userId}");
            var tokenExpiry = GetTokenExpiry(token);
            var tokenData = new CachedTokenData(token, tokenExpiry);
            
            var tokenJson = JsonSerializer.Serialize(tokenData);
            
            var cacheOptions = new DistributedCacheEntryOptions
            {
                // DistributedCacheAdapter only honours AbsoluteExpirationRelativeToNow / SlidingExpiration.
                AbsoluteExpirationRelativeToNow = GetCacheTtl(tokenData.ExpiresAt)
            };
            
            try
            {
                distributedCache.SetString(cacheKey, tokenJson, cacheOptions);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, ">>>>>>>>>> Authentication >>> Failed to cache token for user {UserId}", userId);
            }
        }
        else
        {
            logger.LogWarning(">>>>>>>>>> Authentication >>> Could not identify user ID for token caching");
        }
    }

    public void ClearToken()
    {
        var ctx = httpContextAccessor.HttpContext;
        if (ctx == null) 
        {
            return;
        }

        // Clear from HttpContext.Items
        var wasInItems = ctx.Items.ContainsKey(TokenKey);
        ctx.Items.Remove(TokenKey);

        // Clear from distributed cache
        var userId = GetUserIdFromContext(ctx);
        if (userId != null)
        {
            var cacheKey = GetTenantPrefixedKey($"{CacheKeyPrefix}{userId}");
            
            try
            {
                distributedCache.Remove(cacheKey);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, ">>>>>>>>>> Authentication >>> Failed to clear token cache for user {UserId}", userId);
            }
        }
        else
        {
            logger.LogDebug(">>>>>>>>>> Authentication >>> Could not identify user ID for cache clearing");
        }
    }

    public bool IsTokenValid()
    {
        var token = GetToken();
        return !string.IsNullOrEmpty(token) && IsTokenValid(token);
    }

    public DateTime? GetTokenExpiry()
    {
        var token = GetToken();
        return string.IsNullOrEmpty(token) ? null : GetTokenExpiry(token);
    }

    private static bool IsTokenValid(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            // Use unified 5-minute buffer instead of 1-minute
            var isValid = jwt.ValidTo > DateTime.UtcNow.Add(ExpiryBuffer);
            return isValid;
        }
        catch
        {
            return false;
        }
    }

    private static DateTime GetTokenExpiry(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            return jwt.ValidTo;
        }
        catch
        {
            return DateTime.UtcNow.AddHours(1); // Default fallback
        }
    }

    private static TimeSpan GetCacheTtl(DateTime expiresAt)
    {
        var ttl = expiresAt.Subtract(ExpiryBuffer) - DateTime.UtcNow;
        return ttl > TimeSpan.Zero ? ttl : TimeSpan.FromMinutes(1);
    }

    private static string? GetUserIdFromContext(HttpContext context)
    {
        var user = context.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        // Prefer per-user identity claims. Never use appid/azp — those are client/app IDs
        // and would make every user share the same OBO cache entry.
        return user.FindFirst("preferred_username")?.Value
               ?? user.FindFirst(ClaimTypes.Email)?.Value
               ?? user.FindFirst("email")?.Value
               ?? user.FindFirst("sub")?.Value
               ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? user.Identity?.Name;
    }

    /// <summary>
    /// Cache key used for the OBO JWT. Keep in sync with Application UserCacheInvalidator removals.
    /// </summary>
    public static string BuildCacheKey(Guid? tenantId, string userKey)
    {
        var baseKey = $"{CacheKeyPrefix}{userKey}";
        return tenantId.HasValue ? $"t:{tenantId}:{baseKey}" : baseKey;
    }

    private record CachedTokenData(string Token, DateTime ExpiresAt);
} 