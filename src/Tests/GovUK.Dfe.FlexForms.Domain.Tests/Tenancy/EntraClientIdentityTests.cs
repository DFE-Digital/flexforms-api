using System.Security.Claims;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Xunit;

namespace GovUK.Dfe.FlexForms.Domain.Tests.Tenancy;

public class EntraClientIdentityTests
{
    [Fact]
    public void PickClientId_PrefersAzpOverAppId()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("appid", "v1-app"),
            new Claim("azp", "v2-app")
        ]));

        Assert.Equal("v2-app", EntraClientIdentity.PickClientId(principal));
    }

    [Fact]
    public void PickClientId_UsesSchemaMappedAppId()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(EntraClientIdentity.AppIdSchemaClaimType, "mapped-app")
        ]));

        Assert.Equal("mapped-app", EntraClientIdentity.PickClientId(principal));
    }

    [Theory]
    [InlineData("https://sts.windows.net/abc/", true)]
    [InlineData("https://login.microsoftonline.com/abc/v2.0", true)]
    [InlineData("https://example.com", false)]
    [InlineData(null, false)]
    public void IsEntraIssuer_RecognisesV1AndV2(string? issuer, bool expected)
    {
        Assert.Equal(expected, EntraClientIdentity.IsEntraIssuer(issuer));
    }

    [Fact]
    public void IsAppOnlyToken_TrueWhenIdTypIsApp()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("idtyp", "app"),
            new Claim(ClaimTypes.Email, "ignored@example.com"),
            new Claim("azp", "client")
        ]));

        Assert.True(EntraClientIdentity.IsAppOnlyToken(principal));
    }

    [Fact]
    public void IsAppOnlyToken_FalseWhenIdTypIsUser()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("idtyp", "user"),
            new Claim("azp", "client")
        ]));

        Assert.False(EntraClientIdentity.IsAppOnlyToken(principal));
    }

    [Fact]
    public void IsAppOnlyToken_TrueWhenClientIdPresentAndNoEmail()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("appid", "client")
        ]));

        Assert.True(EntraClientIdentity.IsAppOnlyToken(principal));
    }

    [Fact]
    public void IsAppOnlyToken_FalseWhenInteractiveEmailPresent()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("appid", "client"),
            new Claim(ClaimTypes.Email, "user@example.com")
        ]));

        Assert.False(EntraClientIdentity.IsAppOnlyToken(principal));
    }
}
