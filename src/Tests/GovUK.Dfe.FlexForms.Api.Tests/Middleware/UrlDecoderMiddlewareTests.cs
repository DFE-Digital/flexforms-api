using GovUK.Dfe.FlexForms.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Middleware;

public class UrlDecoderMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_PreservesPlusInEmail_WhenEncodedAsPercent2B()
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(
            "?email=farshad.dashti%2Btr3333%40education.gov.uk");

        string? emailSeenByNext = null;
        var middleware = new UrlDecoderMiddleware(ctx =>
        {
            emailSeenByNext = ctx.Request.Query["email"];
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.Equal("farshad.dashti+tr3333@education.gov.uk", emailSeenByNext);
    }

    [Fact]
    public async Task InvokeAsync_DecodesDoubleEncodedPlus()
    {
        var context = new DefaultHttpContext();
        // First ASP.NET decode: %252B → %2B; middleware: %2B → +
        context.Request.QueryString = new QueryString("?email=user%252Btag%40example.com");

        string? emailSeenByNext = null;
        var middleware = new UrlDecoderMiddleware(ctx =>
        {
            emailSeenByNext = ctx.Request.Query["email"];
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.Equal("user+tag@example.com", emailSeenByNext);
    }

    [Fact]
    public async Task InvokeAsync_LeavesOrdinaryEmailUnchanged()
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?email=user%40example.com");

        string? emailSeenByNext = null;
        var middleware = new UrlDecoderMiddleware(ctx =>
        {
            emailSeenByNext = ctx.Request.Query["email"];
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.Equal("user@example.com", emailSeenByNext);
    }

    [Fact]
    public async Task InvokeAsync_SkipsWhenNoQueryString()
    {
        var context = new DefaultHttpContext();
        var nextCalled = false;
        var middleware = new UrlDecoderMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.False(context.Request.QueryString.HasValue);
    }
}
