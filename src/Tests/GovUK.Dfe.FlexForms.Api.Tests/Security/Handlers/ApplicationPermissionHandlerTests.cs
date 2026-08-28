using System.Security.Claims;
using GovUK.Dfe.FlexForms.Api.Security.Handlers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Security.Handlers;

public class ApplicationPermissionHandlerTests
{
    private static IHttpContextAccessor CreateAccessor(string? applicationId)
    {
        var httpContext = new DefaultHttpContext();
        if (applicationId is not null)
            httpContext.Request.RouteValues["applicationId"] = applicationId;

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        return accessor;
    }

    [Fact]
    public async Task Handle_ShouldSucceed_WhenClaimMatchesRoute()
    {
        var applicationId = Guid.NewGuid().ToString();
        var requirement = new ApplicationPermissionRequirement("Read");
        var accessor = CreateAccessor(applicationId);
        var claims = new[] { new Claim("permission", $"Application:{applicationId}:Read") };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var context = new AuthorizationHandlerContext([requirement], user, null);
        var handler = new ApplicationPermissionHandler(accessor);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Handle_ShouldFail_WhenRouteMissing()
    {
        var requirement = new ApplicationPermissionRequirement("Read");
        var accessor = CreateAccessor(null);
        var claims = new[] { new Claim("permission", "Application:123:Read") };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var context = new AuthorizationHandlerContext([requirement], user, null);
        var handler = new ApplicationPermissionHandler(accessor);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Handle_ShouldFail_WhenClaimMissing()
    {
        var requirement = new ApplicationPermissionRequirement("Read");
        var accessor = CreateAccessor(Guid.NewGuid().ToString());
        var user = new ClaimsPrincipal(new ClaimsIdentity());
        var context = new AuthorizationHandlerContext([requirement], user, null);
        var handler = new ApplicationPermissionHandler(accessor);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Handle_ShouldSucceed_WhenUserIsAdmin()
    {
        var requirement = new ApplicationPermissionRequirement("Read");
        var accessor = CreateAccessor(Guid.NewGuid().ToString());
        var claims = new[] { new Claim(ClaimTypes.Role, "Admin") };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        var context = new AuthorizationHandlerContext([requirement], user, null);
        var handler = new ApplicationPermissionHandler(accessor);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }
}
