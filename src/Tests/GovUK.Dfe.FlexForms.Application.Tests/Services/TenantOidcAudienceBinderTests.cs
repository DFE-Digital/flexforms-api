using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class TenantOidcAudienceBinderTests
{
    [Fact]
    public void TokenMatchesTenant_ReturnsTrue_WhenNoOidcConfigured()
    {
        var binder = new TenantOidcAudienceBinder();
        var tenant = CreateTenant();

        Assert.True(binder.TokenMatchesTenant(tenant, new[] { "anything" }));
    }

    [Fact]
    public void TokenMatchesTenant_ReturnsTrue_WhenAudienceMatchesDfESignInClientId()
    {
        var binder = new TenantOidcAudienceBinder();
        var tenant = CreateTenant(("DfESignIn:ClientId", "transfers-client"));

        Assert.True(binder.TokenMatchesTenant(tenant, new[] { "transfers-client" }));
    }

    [Fact]
    public void TokenMatchesTenant_ReturnsFalse_WhenAudienceBelongsToAnotherTenant()
    {
        var binder = new TenantOidcAudienceBinder();
        var tenant = CreateTenant(("DfESignIn:ClientId", "transfers-client"));

        Assert.False(binder.TokenMatchesTenant(tenant, new[] { "lsrp-client" }));
    }

    private static TenantConfiguration CreateTenant(params (string Key, string Value)[] settings)
    {
        var dict = settings.ToDictionary(x => x.Key, x => (string?)x.Value);
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        return new TenantConfiguration(Guid.NewGuid(), "Test", config, Array.Empty<string>());
    }
}
