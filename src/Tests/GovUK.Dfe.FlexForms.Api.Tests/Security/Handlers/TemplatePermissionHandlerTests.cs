using System.Security.Claims;
using GovUK.Dfe.FlexForms.Api.Security.Handlers;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Security.Handlers;

public class TemplatePermissionHandlerTests
{
    private static IHttpContextAccessor CreateAccessor(string? templateId, bool belongsToTenant = true)
    {
        var httpContext = new DefaultHttpContext();
        if (templateId is not null)
            httpContext.Request.RouteValues["templateId"] = templateId;

        var resolver = Substitute.For<ITenantTemplateResolver>();
        resolver.IsTemplateInCurrentTenantAsync(Arg.Any<TemplateId>(), Arg.Any<CancellationToken>())
            .Returns(belongsToTenant);

        var services = new ServiceCollection();
        services.AddSingleton(resolver);
        httpContext.RequestServices = services.BuildServiceProvider();

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        return accessor;
    }

    [Fact]
    public async Task Handle_ShouldSucceed_WhenClaimMatchesRoute()
    {
        var templateId = Guid.NewGuid().ToString();
        var requirement = new TemplatePermissionRequirement("Read");
        var accessor = CreateAccessor(templateId);
        var claims = new[] { new Claim("permission", $"Template:{templateId}:Read") };
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
        var accessor = CreateAccessor(Guid.NewGuid().ToString());
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
        var accessor = CreateAccessor(Guid.NewGuid().ToString());
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
        var accessor = CreateAccessor(Guid.NewGuid().ToString());
        var claims = new[] { new Claim("permission", "Application:Any:Read") };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var context = new AuthorizationHandlerContext([requirement], user, null);
        var handler = new TemplatePermissionHandler(accessor);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Handle_ShouldFail_WhenTemplateBelongsToAnotherTenant()
    {
        var templateId = Guid.NewGuid().ToString();
        var requirement = new TemplatePermissionRequirement("Read");
        var accessor = CreateAccessor(templateId, belongsToTenant: false);
        var claims = new[]
        {
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim("permission", $"Template:{templateId}:Read")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims));
        var context = new AuthorizationHandlerContext([requirement], user, null);
        var handler = new TemplatePermissionHandler(accessor);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }
}
