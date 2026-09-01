using GovUK.Dfe.FlexForms.Api.Security;
using GovUK.Dfe.FlexForms.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Security;

public class PlatformBearerSchemeSelectionTests
{
    [Theory]
    [InlineData("/v1/tenant-config/resolve")]
    [InlineData("/v1/tenant-config/resolve?hostname=visits.example")]
    [InlineData("/v1/tenant-config/tenants/33333333-3333-4333-8333-333333333333")]
    [InlineData("/v1/host-config")]
    public void EndpointRequiresPlatformBearerOnly_IsTrue_ForBootstrapPaths(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path.Split('?')[0];
        context.Request.QueryString = path.Contains('?', StringComparison.Ordinal)
            ? new QueryString(path[path.IndexOf('?', StringComparison.Ordinal)..])
            : QueryString.Empty;

        Assert.True(AuthorizationExtensions.EndpointRequiresPlatformBearerOnly(context));
    }

    [Theory]
    [InlineData("/v1/tenant-config")]
    [InlineData("/v1/Applications")]
    [InlineData("/v1/admin/tenants")]
    public void EndpointRequiresPlatformBearerOnly_IsFalse_ForTenantApiPaths(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        Assert.False(AuthorizationExtensions.EndpointRequiresPlatformBearerOnly(context));
    }

    [Fact]
    public void EndpointRequiresPlatformBearerOnly_IgnoresEmptyClassAuthorize_WhenMethodHasPlatformPolicy()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/v1/admin/tenants/seed";
        context.Features.Set<IEndpointFeature>(new StubEndpointFeature(
            new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(
                    new AuthorizeAttribute(),
                    new AuthorizeAttribute(PlatformConstants.PlatformTenantConfigPolicy)),
                "seed")));

        Assert.True(AuthorizationExtensions.EndpointRequiresPlatformBearerOnly(context));
    }

    private sealed class StubEndpointFeature(Endpoint endpoint) : IEndpointFeature
    {
        public Endpoint? Endpoint { get; set; } = endpoint;
    }
}
