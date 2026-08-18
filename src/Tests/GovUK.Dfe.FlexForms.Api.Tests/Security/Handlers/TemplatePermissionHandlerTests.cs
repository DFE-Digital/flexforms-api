using System.Security.Claims;
using GovUK.Dfe.FlexForms.Api.Security.Handlers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Security.Handlers;

public class TemplatePermissionHandlerTests
{
    [Fact]
    public async Task Handle_ShouldSucceed_WhenClaimMatchesRoute()
    {
        var requirement = new TemplatePermissionRequirement("Read");
        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues["templateId"] = "t1";
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var claims = new[] { new Claim("permission", "Template:t1:Read") };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var context = new AuthorizationHandlerContext([requirement], user, null);
        var handler = new TemplatePermissionHandler(accessor);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Handle_ShouldFail_WhenClaimMissing()
    {
        var requirement = new TemplatePermissionRequirement("Read");
        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues["templateId"] = "t1";
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var user = new ClaimsPrincipal(new ClaimsIdentity());
        var context = new AuthorizationHandlerContext([requirement], user, null);
        var handler = new TemplatePermissionHandler(accessor);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Handle_ShouldSucceed_WhenUserHasApplicationAnyReadClaim()
    {
        var requirement = new TemplatePermissionRequirement("Read");
        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues["templateId"] = "t1";
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var claims = new[] { new Claim("permission", "Application:Any:Read") };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var context = new AuthorizationHandlerContext([requirement], user, null);
        var handler = new TemplatePermissionHandler(accessor);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Handle_ShouldNotGrantWrite_WhenUserHasApplicationAnyReadClaim()
    {
        var requirement = new TemplatePermissionRequirement("Write");
        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues["templateId"] = "t1";
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var claims = new[] { new Claim("permission", "Application:Any:Read") };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var context = new AuthorizationHandlerContext([requirement], user, null);
        var handler = new TemplatePermissionHandler(accessor);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }
}