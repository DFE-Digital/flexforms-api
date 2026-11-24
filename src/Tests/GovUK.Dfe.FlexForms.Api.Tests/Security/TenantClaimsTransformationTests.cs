using System.Security.Claims;
using GovUK.Dfe.FlexForms.Api.Security;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Security;

public class TenantClaimsTransformationTests
{
    private static readonly Guid ProviderTenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public async Task SignedOutPrincipal_IsLeftAlone()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var http = new DefaultHttpContext();
        http.Items[AuthConstants.MatchedAuthProviderKey] = Provider(isServicePrincipal: true, roles: ["SuperAdmin"]);

        var result = await Transform(http, principal);

        Assert.Empty(result.Claims);
    }

    [Fact]
    public async Task MatchedProvider_AddsItsTenantServiceFlagAndRoles()
    {
        var http = new DefaultHttpContext();
        http.Items[AuthConstants.MatchedAuthProviderKey] = Provider(isServicePrincipal: true, roles: ["Reader", "Writer"]);

        var result = await Transform(http, SignedIn(new Claim(ClaimTypes.Role, "Reader")));

        Assert.Equal(ProviderTenantId.ToString(), result.FindFirstValue(TenantAuthClaimTypes.TenantId));
        Assert.Equal("true", result.FindFirstValue(TenantAuthClaimTypes.IsService));
        Assert.Equal(["Reader", "Writer"], result.FindAll(ClaimTypes.Role).Select(c => c.Value).Order());
    }

    [Fact]
    public async Task UserProvider_IsMarkedAsNotAService()
    {
        var http = new DefaultHttpContext();
        http.Items[AuthConstants.MatchedAuthProviderKey] = Provider(isServicePrincipal: false, roles: []);

        var result = await Transform(http, SignedIn());

        Assert.Equal("false", result.FindFirstValue(TenantAuthClaimTypes.IsService));
    }

    [Fact]
    public async Task ExistingTenantAndServiceClaims_AreKept()
    {
        var http = new DefaultHttpContext();
        http.Items[AuthConstants.MatchedAuthProviderKey] = Provider(isServicePrincipal: true, roles: []);
        var other = Guid.NewGuid().ToString();

        var result = await Transform(http, SignedIn(
            new Claim(TenantAuthClaimTypes.TenantId, other),
            new Claim(TenantAuthClaimTypes.IsService, "false")));

        Assert.Equal([other], result.FindAll(TenantAuthClaimTypes.TenantId).Select(c => c.Value));
        Assert.Equal(["false"], result.FindAll(TenantAuthClaimTypes.IsService).Select(c => c.Value));
    }

    [Fact]
    public async Task NoMatchedProvider_UsesTheTokensOwnClaims()
    {
        var directoryTenant = Guid.NewGuid().ToString();

        var result = await Transform(new DefaultHttpContext(), SignedIn(
            new Claim(TenantAuthClaimTypes.AzureDirectoryTenantId, directoryTenant),
            new Claim(TenantAuthClaimTypes.Email, "user@example.com")));

        Assert.Equal(directoryTenant, result.FindFirstValue(TenantAuthClaimTypes.TenantId));
        Assert.Null(result.FindFirst(TenantAuthClaimTypes.IsService));
        Assert.Equal("user@example.com", result.FindFirstValue(ClaimTypes.Email));
    }

    [Fact]
    public async Task NoHttpContext_StillNormalisesTheEmail()
    {
        var result = await Transform(null, SignedIn(new Claim(TenantAuthClaimTypes.PreferredUsername, "upn@example.com")));

        Assert.Equal("upn@example.com", result.FindFirstValue(ClaimTypes.Email));
    }

    private static Task<ClaimsPrincipal> Transform(HttpContext? http, ClaimsPrincipal principal)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(http);
        return new TenantClaimsTransformation(accessor).TransformAsync(principal);
    }

    private static ClaimsPrincipal SignedIn(params Claim[] claims) => new(new ClaimsIdentity(claims, "Bearer"));

    private static TenantAuthProvider Provider(bool isServicePrincipal, string[] roles) =>
        new(ProviderTenantId, "provider", default, isServicePrincipal, "https://issuer", "https://issuer", "", [], "client", "", "", "", roles);
}
