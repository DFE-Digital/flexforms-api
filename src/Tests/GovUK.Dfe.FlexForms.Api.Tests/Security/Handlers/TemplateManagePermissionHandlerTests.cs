using System.Security.Claims;
using GovUK.Dfe.FlexForms.Api.Security.Handlers;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Security.Handlers;

/// <summary>
/// Template Manage on a <c>{templateId}</c> route, and on routes without one.
/// A manager of one template on another template's route is not asserted here.
/// </summary>
public class TemplateManagePermissionHandlerTests
{
    private static readonly Guid TemplateId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    private readonly ITenantTemplateResolver _resolver = Substitute.For<ITenantTemplateResolver>();

    public TemplateManagePermissionHandlerTests()
    {
        _resolver.IsTemplateInCurrentTenantAsync(new TemplateId(TemplateId), Arg.Any<CancellationToken>()).Returns(true);
    }

    public static TheoryData<string, Claim[]> Managers() => new()
    {
        { "exact template", [Permission($"Template:{TemplateId}:Manage")] },
        { "any template", [Permission("Template:Any:Manage")] },
        { "Admin", [new Claim(ClaimTypes.Role, RoleNames.Admin)] },
        { "SuperAdmin", [new Claim(ClaimTypes.Role, RoleNames.SuperAdmin)] },
    };

    [Theory]
    [MemberData(nameof(Managers))]
    public async Task TemplateRoute_ManagerOfThatTemplateSucceeds(string _, Claim[] claims)
    {
        Assert.True(await AuthorizeAsync(claims, TemplateId.ToString()));
    }

    public static TheoryData<string, Claim[]> NonManagers() => new()
    {
        { "no claims", [] },
        { "read only", [Permission($"Template:{TemplateId}:Read")] },
        { "write only", [Permission($"Template:{TemplateId}:Write")] },
        { "user manager", [Permission("User:Any:Manage")] },
        { "application manage", [Permission($"Application:{TemplateId}:Manage")] },
    };

    [Theory]
    [MemberData(nameof(NonManagers))]
    public async Task TemplateRoute_NonManagerIsDenied(string _, Claim[] claims)
    {
        Assert.False(await AuthorizeAsync(claims, TemplateId.ToString()));
    }

    [Fact]
    public async Task TemplateRoute_TemplateInAnotherTenantIsDeniedEvenForAnAdmin()
    {
        _resolver.IsTemplateInCurrentTenantAsync(new TemplateId(TemplateId), Arg.Any<CancellationToken>()).Returns(false);

        Assert.False(await AuthorizeAsync([new Claim(ClaimTypes.Role, RoleNames.Admin)], TemplateId.ToString()));
    }

    [Fact]
    public async Task TemplateRoute_WithoutATenantTemplateResolverIsDenied()
    {
        Assert.False(await AuthorizeAsync([new Claim(ClaimTypes.Role, RoleNames.Admin)], TemplateId.ToString(), registerResolver: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public async Task NoUsableTemplateRoute_AdminOrAnyTemplateManagerSucceeds(string? routeValue)
    {
        Assert.True(await AuthorizeAsync([new Claim(ClaimTypes.Role, RoleNames.Admin)], routeValue));
        Assert.True(await AuthorizeAsync([Permission("Template:Any:Manage")], routeValue));
        await _resolver.DidNotReceiveWithAnyArgs().IsTemplateInCurrentTenantAsync(default!, default);
    }

    [Fact]
    public async Task NoTemplateRoute_NonManagerIsDenied()
    {
        Assert.False(await AuthorizeAsync([Permission($"Template:{TemplateId}:Read")], templateRouteValue: null));
    }

    private async Task<bool> AuthorizeAsync(Claim[] claims, string? templateRouteValue, bool registerResolver = true)
    {
        var services = new ServiceCollection();
        if (registerResolver)
            services.AddSingleton(_resolver);

        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        if (templateRouteValue is not null)
            http.Request.RouteValues["templateId"] = templateRouteValue;

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(http);

        var requirement = new TemplateManagePermissionRequirement();
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, "user@example.com"), .. claims], "Test"));
        var context = new AuthorizationHandlerContext([requirement], user, null);

        await new TemplateManagePermissionHandler(accessor).HandleAsync(context);
        return context.HasSucceeded;
    }

    private static Claim Permission(string value) => new("permission", value);
}
