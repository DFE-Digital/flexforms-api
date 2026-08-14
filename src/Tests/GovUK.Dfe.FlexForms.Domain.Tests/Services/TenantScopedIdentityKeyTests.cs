using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Services;

namespace GovUK.Dfe.FlexForms.Domain.Tests.Services;

public class TenantScopedIdentityKeyTests
{
    [Fact]
    public void Combine_PrefixesTenantId()
    {
        var tenantId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var key = TenantScopedIdentityKey.Combine(tenantId, "user@example.com");

        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee:user@example.com", key);
    }

    [Fact]
    public void Combine_DoesNotDoublePrefix()
    {
        var tenantId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var prefixed = TenantScopedIdentityKey.Combine(tenantId, "user@example.com");

        var again = TenantScopedIdentityKey.Combine(tenantId, prefixed);

        Assert.Equal(prefixed, again);
    }

    [Fact]
    public void TrySplit_ParsesPrefixedKey()
    {
        var tenantId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var key = TenantScopedIdentityKey.Combine(tenantId, "user@example.com");

        Assert.True(TenantScopedIdentityKey.TrySplit(key, out var parsedTenant, out var identity));
        Assert.Equal(tenantId, parsedTenant);
        Assert.Equal("user@example.com", identity);
    }

    [Fact]
    public void TrySplit_ReturnsFalse_ForPlainEmail()
    {
        Assert.False(TenantScopedIdentityKey.TrySplit("user@example.com", out _, out var identity));
        Assert.Equal("user@example.com", identity);
    }

    [Fact]
    public void ToClaimResourceKey_StripsTenantPrefixForNotifications()
    {
        var tenantId = Guid.NewGuid();
        var key = TenantScopedIdentityKey.Combine(tenantId, "user@example.com");

        Assert.Equal(
            "user@example.com",
            TenantScopedIdentityKey.ToClaimResourceKey(ResourceType.Notifications, key));
        Assert.Equal(
            key,
            TenantScopedIdentityKey.ToClaimResourceKey(ResourceType.User, key));
    }

    [Fact]
    public void NotificationsBelongToTenant_MatchesOnlyThatTenant()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var key = TenantScopedIdentityKey.Combine(tenantId, "user@example.com");

        Assert.True(TenantScopedIdentityKey.NotificationsBelongToTenant(key, tenantId));
        Assert.False(TenantScopedIdentityKey.NotificationsBelongToTenant(key, otherTenantId));
        Assert.False(TenantScopedIdentityKey.NotificationsBelongToTenant("user@example.com", tenantId));
    }
}
