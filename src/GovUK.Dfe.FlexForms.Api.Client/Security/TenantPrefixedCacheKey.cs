using System;

namespace GovUK.Dfe.FlexForms.Api.Client.Security;

/// <summary>
/// Builds Redis keys that always include a tenant id. Callers without a tenant must not cache.
/// </summary>
public static class TenantPrefixedCacheKey
{
    /// <summary>
    /// Returns <c>t:{tenantId}:{key}</c>. Throws when <paramref name="tenantId"/> is missing.
    /// Does not double-prefix keys that already use that format.
    /// </summary>
    public static string Create(Guid? tenantId, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Cache key is required.", nameof(key));

        if (tenantId is null)
        {
            throw new InvalidOperationException(
                "Cannot create a tenant-scoped cache key because ApiClientSettings.TenantId is not set.");
        }

        if (IsAlreadyPrefixed(key))
            return key;

        return $"t:{tenantId}:{key}";
    }

    internal static bool IsAlreadyPrefixed(string key)
    {
        const int guidLength = 36;
        const int prefixLength = 2 + guidLength + 1;
        return key.Length >= prefixLength
               && key.StartsWith("t:", StringComparison.Ordinal)
               && key[38] == ':'
               && Guid.TryParse(key.AsSpan(2, guidLength), out _);
    }
}
