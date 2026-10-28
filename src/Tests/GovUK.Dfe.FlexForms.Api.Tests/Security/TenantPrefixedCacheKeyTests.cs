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
}
