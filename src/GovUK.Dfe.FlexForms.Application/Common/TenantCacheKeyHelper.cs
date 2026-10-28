using GovUK.Dfe.FlexForms.Domain.Tenancy;

namespace GovUK.Dfe.FlexForms.Application.Common;

/// <summary>
/// Helper for creating tenant-scoped cache keys.
/// Use this to prefix cache keys with the current tenant ID for multi-tenant isolation.
/// </summary>
public static class TenantCacheKeyHelper
{
    /// <summary>
    /// Creates a tenant-scoped cache key by prefixing with the tenant ID.
    /// Throws when no tenant is on the context — callers that genuinely have no tenant
    /// must not use this helper.
    /// </summary>
    /// <example>
    /// Input: "Applications_ForUser_abc123"
    /// Output: "t:550e8400-e29b-41d4-a716-446655440000:Applications_ForUser_abc123"
    /// </example>
    public static string CreateTenantScopedKey(ITenantContextAccessor? tenantContextAccessor, string baseCacheKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseCacheKey);

        var tenant = tenantContextAccessor?.CurrentTenant
            ?? throw new InvalidOperationException(
                "Cannot create a tenant-scoped cache key because no tenant is on the current context.");

        if (TryGetPrefixedTenantId(baseCacheKey, out var prefixedTenantId))
        {
            if (prefixedTenantId != tenant.Id)
            {
                throw new InvalidOperationException(
                    $"Cache key is scoped to tenant {prefixedTenantId}, not the current tenant {tenant.Id}.");
            }

            return baseCacheKey;
        }

        return $"t:{tenant.Id}:{baseCacheKey}";
    }

    internal static bool TryGetPrefixedTenantId(string key, out Guid tenantId)
    {
        const int guidLength = 36;
        const int prefixLength = 2 + guidLength + 1;
        tenantId = default;
        return key.Length >= prefixLength
               && key.StartsWith("t:", StringComparison.Ordinal)
               && key[38] == ':'
               && Guid.TryParse(key.AsSpan(2, guidLength), out tenantId);
    }
}
