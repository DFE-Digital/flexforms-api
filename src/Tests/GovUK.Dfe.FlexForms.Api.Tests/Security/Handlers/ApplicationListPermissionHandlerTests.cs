using System.Security.Claims;
using GovUK.Dfe.FlexForms.Api.Security.Handlers;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Security.Handlers;

public class ApplicationListPermissionHandlerTests
{
    [Fact]
    public async Task Handle_ShouldSucceed_WhenUserHasApplicationPermission()
    {
        var requirement = new ApplicationListPermissionRequirement("Read");
        var claims = new[] { new Claim("permission", "Application:123:Read") };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var context = new AuthorizationHandlerContext([requirement], user, null);
        var handler = new ApplicationListPermissionHandler();

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Handle_ShouldFail_WhenUserLacksPermission()
    {
        var requirement = new ApplicationListPermissionRequirement("Read");
        var claims = new[] { new Claim("permission", "Application:123:Write") };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var context = new AuthorizationHandlerContext([requirement], user, null);
        var handler = new ApplicationListPermissionHandler();

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Handle_ShouldSucceed_WhenUserHasTemplateReadPermission()
    {
        var requirement = new ApplicationListPermissionRequirement("Read");
        var claims = new[] { new Claim("permission", "Template:123:Read") };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var context = new AuthorizationHandlerContext([requirement], user, null);
        var handler = new ApplicationListPermissionHandler();

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Handle_ShouldSucceed_WhenUserHasApplicationAnyReadWildcard()
    {
        var requirement = new ApplicationListPermissionRequirement("Read");
        var claims = new[] { new Claim("permission", "Application:Any:Read") };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var context = new AuthorizationHandlerContext([requirement], user, null);
        var handler = new ApplicationListPermissionHandler();

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }
}