using GovUK.Dfe.FlexForms.Application.TenantAdmin;
using Microsoft.Extensions.Configuration;

namespace GovUK.Dfe.FlexForms.Application.Tests.TenantAdmin;

public class TenantInteractiveAuthSchemeResolverTests
{
    [Fact]
    public void ResolveSchemeName_ShouldPreferExplicitScheme_OverTestEnabled()
    {
        var settings = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Authentication:Scheme"] = "DfESignIn",
            ["TestAuthentication:Enabled"] = "true"
        });

        var scheme = TenantInteractiveAuthSchemeResolver.ResolveSchemeName(settings);

        Assert.Equal("DfESignIn", scheme);
    }

    [Fact]
    public void ResolveSchemeName_ShouldMapDsiAlias_ToDfESignIn()
    {
        var settings = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Authentication:Scheme"] = "DSI"
        });

        var scheme = TenantInteractiveAuthSchemeResolver.ResolveSchemeName(settings);

        Assert.Equal("DfESignIn", scheme);
    }

    [Fact]
    public void ResolveSchemeName_ShouldDefaultToDfESignIn_WhenNoFlagsEnabled()
    {
        var settings = BuildConfiguration(new Dictionary<string, string?>());

        var scheme = TenantInteractiveAuthSchemeResolver.ResolveSchemeName(settings);

        Assert.Equal("DfESignIn", scheme);
    }

    private static IConfiguration BuildConfiguration(IReadOnlyDictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
