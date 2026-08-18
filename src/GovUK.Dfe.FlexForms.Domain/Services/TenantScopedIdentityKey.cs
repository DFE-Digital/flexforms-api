using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;

namespace GovUK.Dfe.FlexForms.Domain.Services;

/// <summary>
/// Prefixes a per-user identity (email or client id) with a tenant id so grants and
/// notification storage keys stay isolated when the same user exists on multiple tenants.
/// Stored form is <c>{tenantId:D}:{identity}</c>.
/// </summary>
public static class TenantScopedIdentityKey
{
    public const char Separator = ':';

    public static string Combine(Guid tenantId, string identity)
    {
        var value = identity?.Trim() ?? string.Empty;
        if (TrySplit(value, out _, out var existingIdentity))
            value = existingIdentity;

        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Identity is required.", nameof(identity));

        return $"{tenantId:D}{Separator}{value}";
    }

    public static bool TrySplit(string? resourceKey, out Guid tenantId, out string identity)
    {
        tenantId = default;
        identity = resourceKey ?? string.Empty;

        if (string.IsNullOrWhiteSpace(resourceKey))
            return false;

        var separator = resourceKey.IndexOf(Separator);
        if (separator != 36 || separator >= resourceKey.Length - 1)
            return false;

        if (!Guid.TryParse(resourceKey[..separator], out tenantId))
            return false;

        identity = resourceKey[(separator + 1)..];
        return !string.IsNullOrWhiteSpace(identity);
    }

    public static string ToClaimResourceKey(ResourceType resourceType, string resourceKey)
    {
        if (resourceType != ResourceType.Notifications)
            return resourceKey;

        return TrySplit(resourceKey, out _, out var identity) ? identity : resourceKey;
    }

    public static bool NotificationsBelongToTenant(string resourceKey, Guid tenantId)
    {
        if (TrySplit(resourceKey, out var scopedTenantId, out _))
            return scopedTenantId == tenantId;

        // Legacy unscoped email/client-id keys predate tenant prefixes. Dropping them
        // silently removed notification access for existing users (including lead applicants).
        return !string.IsNullOrWhiteSpace(resourceKey);
    }
}
