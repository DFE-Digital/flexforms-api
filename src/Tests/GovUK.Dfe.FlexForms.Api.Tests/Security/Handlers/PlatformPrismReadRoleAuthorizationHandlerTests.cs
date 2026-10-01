using System.Security.Claims;
using GovUK.Dfe.FlexForms.Api.Security.Handlers;
using GovUK.Dfe.FlexForms.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Security.Handlers;

public class PlatformPrismReadRoleAuthorizationHandlerTests
{
    [Theory]
    [InlineData("roles", "Prism.Read")]
    [InlineData("roles", "prism.read")]
    [InlineData(ClaimTypes.Role, "Prism.Read")]
    public async Task HandleAsync_ShouldSucceed_WhenPrismReadRolePresent(string claimType, string value)
    {
        var context = await AuthorizeAsync(new Claim(claimType, value));

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_ShouldSucceed_WhenPrismReadIsOneOfSeveralRoles()
    {
        var context = await AuthorizeAsync(
            new Claim("roles", PlatformConstants.TenantConfigReadAppRole),
            new Claim("roles", PlatformConstants.PrismReadAppRole));

        Assert.True(context.HasSucceeded);
    }

    [Theory]
    [InlineData("roles", "Prism.Admin")]
    [InlineData("roles", "Prism.Read.All")]
    [InlineData("scp", "Prism.Read")]
    [InlineData("name", "Prism.Read")]
    public async Task HandleAsync_ShouldNotSucceed_WithoutThePrismReadAppRole(string claimType, string value)
    {
        var context = await AuthorizeAsync(new Claim(claimType, value));

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_ShouldNotSucceed_WhenPrincipalHasNoClaims()
    {
        var context = await AuthorizeAsync();

        Assert.False(context.HasSucceeded);
    }

    private static async Task<AuthorizationHandlerContext> AuthorizeAsync(params Claim[] claims)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer"));
        var context = new AuthorizationHandlerContext([new PlatformPrismReadRoleRequirement()], user, resource: null);

        await new PlatformPrismReadRoleAuthorizationHandler().HandleAsync(context);

        return context;
    }
}
