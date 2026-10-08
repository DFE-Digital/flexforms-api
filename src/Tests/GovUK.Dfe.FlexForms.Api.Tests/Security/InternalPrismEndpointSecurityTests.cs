using System.Reflection;
using GovUK.Dfe.FlexForms.Api.Controllers;
using GovUK.Dfe.FlexForms.Api.Security;
using GovUK.Dfe.FlexForms.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Security;

/// <summary>
/// The internal Prism endpoints are called by the Prism Function's managed identity with a platform token, so they
/// must authenticate with PlatformBearer and require the Prism.Read app role on every action.
/// </summary>
public class InternalPrismEndpointSecurityTests
{
    public static TheoryData<string> Actions()
    {
        var data = new TheoryData<string>();
        foreach (var action in ActionMethods())
            data.Add(action.Name);
        return data;
    }

    [Fact]
    public void Controller_RequiresThePrismReadPolicy()
    {
        var authorize = Assert.Single(typeof(InternalPrismController).GetCustomAttributes<AuthorizeAttribute>(inherit: true));

        Assert.Equal(PlatformConstants.PlatformPrismReadPolicy, authorize.Policy);
        Assert.Empty(typeof(InternalPrismController).GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public void Action_DoesNotWeakenOrMixTheControllerPolicy(string actionName)
    {
        var action = ActionMethods().Single(m => m.Name == actionName);

        Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
        Assert.All(
            action.GetCustomAttributes<AuthorizeAttribute>(inherit: true),
            a => Assert.True(string.IsNullOrEmpty(a.Policy) || a.Policy == PlatformConstants.PlatformPrismReadPolicy));
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public void Action_SelectsPlatformBearer(string actionName)
    {
        var action = ActionMethods().Single(m => m.Name == actionName);
        var template = action.GetCustomAttribute<HttpMethodAttribute>()!.Template;

        var context = ContextFor($"/v1/internal/prism/{template}", EndpointMetadataFor(action));

        Assert.True(AuthorizationExtensions.EndpointRequiresPlatformBearerOnly(context));
    }

    [Fact]
    public void PrismReadPolicy_MixedWithATenantPolicy_DoesNotSelectPlatformBearer()
    {
        var context = ContextFor(
            "/v1/internal/prism/tenants",
            [
                new AuthorizeAttribute(PlatformConstants.PlatformPrismReadPolicy),
                new AuthorizeAttribute("ServiceCallers"),
            ]);

        Assert.False(AuthorizationExtensions.EndpointRequiresPlatformBearerOnly(context));
    }

    [Fact]
    public void PrismPath_WithoutEndpointMetadata_DoesNotSelectPlatformBearer()
    {
        var context = ContextFor("/v1/internal/prism/tenants", metadata: null);

        Assert.False(AuthorizationExtensions.EndpointRequiresPlatformBearerOnly(context));
    }

    private static IEnumerable<MethodInfo> ActionMethods() =>
        typeof(InternalPrismController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<HttpMethodAttribute>() is not null);

    /// <summary>Controller attributes first, then action attributes, as MVC orders endpoint metadata.</summary>
    private static object[] EndpointMetadataFor(MethodInfo action) =>
    [
        .. typeof(InternalPrismController).GetCustomAttributes(inherit: true),
        .. action.GetCustomAttributes(inherit: true),
    ];

    private static DefaultHttpContext ContextFor(string path, object[]? metadata)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (metadata is not null)
        {
            context.Features.Set<IEndpointFeature>(new StubEndpointFeature(
                new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), path)));
        }

        return context;
    }

    private sealed class StubEndpointFeature(Endpoint endpoint) : IEndpointFeature
    {
        public Endpoint? Endpoint { get; set; } = endpoint;
    }
}
