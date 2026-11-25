using System.Security.Claims;
using GovUK.Dfe.FlexForms.Api.Security.Handlers;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Security.Handlers;

public class ServicePrincipalHandlerTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task MatchedProvider_DecidesWhetherTheCallerIsAService(bool isServicePrincipal, bool expected)
    {
        var http = new DefaultHttpContext();
        http.Items[AuthConstants.MatchedAuthProviderKey] = Provider(isServicePrincipal);

        Assert.Equal(expected, await AuthorizeAsync(http, User()));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    public async Task WithoutAMatchedProvider_FallsBackToTheIsServiceClaim(string value, bool expected)
    {
        Assert.Equal(expected, await AuthorizeAsync(new DefaultHttpContext(), User(new Claim(TenantAuthClaimTypes.IsService, value))));
    }

    [Fact]
    public async Task NoHttpContextAndNoClaim_IsDenied()
    {
        Assert.False(await AuthorizeAsync(null, User()));
    }

    [Fact]
    public async Task SomethingElseInTheProviderSlot_IsIgnored()
    {
        var http = new DefaultHttpContext();
        http.Items[AuthConstants.MatchedAuthProviderKey] = "not a provider";

        Assert.False(await AuthorizeAsync(http, User()));
    }

    private static async Task<bool> AuthorizeAsync(HttpContext? http, ClaimsPrincipal user)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(http);
        var requirement = new ServicePrincipalRequirement();
        var context = new AuthorizationHandlerContext([requirement], user, null);

        await new ServicePrincipalHandler(accessor).HandleAsync(context);
        return context.HasSucceeded;
    }

    private static ClaimsPrincipal User(params Claim[] claims) => new(new ClaimsIdentity(claims, "Test"));

    private static TenantAuthProvider Provider(bool isServicePrincipal) =>
        new(Guid.NewGuid(), "provider", default, isServicePrincipal, "https://issuer", "https://issuer", "", [], "client", "", "", "", []);
}

public class UserManagePermissionHandlerTests
{
    [Theory]
    [InlineData(ClaimTypes.Role, RoleNames.Admin)]
    [InlineData(ClaimTypes.Role, RoleNames.SuperAdmin)]
    [InlineData("permission", "User:Any:Manage")]
    [InlineData("permission", "User:11111111-1111-4111-8111-111111111111:Manage")]
    public async Task AdminOrAnyUserManageGrant_Succeeds(string type, string value)
    {
        Assert.True(await AuthorizeAsync(new Claim(type, value)));
    }

    [Theory]
    [InlineData("permission", "User:Any:Read")]
    [InlineData("permission", "User:Any:Write")]
    [InlineData("permission", "Template:Any:Manage")]
    [InlineData(ClaimTypes.Role, "User")]
    public async Task AnythingElse_IsDenied(string type, string value)
    {
        Assert.False(await AuthorizeAsync(new Claim(type, value)));
    }

    private static async Task<bool> AuthorizeAsync(Claim claim)
    {
        var requirement = new UserManagePermissionRequirement();
        var user = new ClaimsPrincipal(new ClaimsIdentity([claim], "Test"));
        var context = new AuthorizationHandlerContext([requirement], user, null);

        await new UserManagePermissionHandler().HandleAsync(context);
        return context.HasSucceeded;
    }
}
