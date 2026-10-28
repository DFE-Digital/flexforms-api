using GovUK.Dfe.FlexForms.Api.Client.Security;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Security;

public class TenantPrefixedCacheKeyTests
{
    [Fact]
    public void Create_ShouldPrefixWithTenantId()
    {
        var tenantId = Guid.NewGuid();

        var result = TenantPrefixedCacheKey.Create(tenantId, "obo_token_user");

        Assert.Equal($"t:{tenantId}:obo_token_user", result);
    }

    [Fact]
    public void Create_ShouldThrow_WhenTenantIdIsNull()
    {
        Assert.Throws<InvalidOperationException>(
            () => TenantPrefixedCacheKey.Create(null, "obo_token_user"));
    }

    [Fact]
    public void Create_ShouldNotDoublePrefix()
    {
        var tenantId = Guid.NewGuid();
        var existing = $"t:{tenantId}:obo_token_user";

        Assert.Equal(existing, TenantPrefixedCacheKey.Create(tenantId, existing));
    }

    [Fact]
    public void Create_ShouldThrow_WhenPrefixedForADifferentTenant()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var foreignKey = $"t:{otherTenantId}:obo_token_user";

        var ex = Assert.Throws<InvalidOperationException>(
            () => TenantPrefixedCacheKey.Create(tenantId, foreignKey));

        Assert.Contains(otherTenantId.ToString(), ex.Message);
        Assert.Contains(tenantId.ToString(), ex.Message);
    }
}
