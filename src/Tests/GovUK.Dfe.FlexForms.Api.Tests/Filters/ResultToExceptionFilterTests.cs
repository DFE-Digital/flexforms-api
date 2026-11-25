using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Api.Filters;
using GovUK.Dfe.FlexForms.Application.Common.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Filters;

public class ResultToExceptionFilterTests
{
    private readonly ResultToExceptionFilter _filter = new();
    private bool _nextCalled;

    [Theory]
    [InlineData(null, 200)]
    [InlineData(201, 201)]
    public async Task SuccessfulResult_IsUnwrappedToItsValue(int? statusCode, int expectedStatus)
    {
        var context = Context(new ObjectResult(Result<string>.Success("payload")) { StatusCode = statusCode });

        await _filter.OnResultExecutionAsync(context, NextAsync(context));

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal("payload", result.Value);
        Assert.Equal(expectedStatus, result.StatusCode);
        Assert.True(_nextCalled);
    }

    [Fact]
    public async Task ForbiddenResult_ThrowsForbidden()
    {
        var context = Context(new ObjectResult(Result<string>.Forbid("No access")));

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => _filter.OnResultExecutionAsync(context, NextAsync(context)));

        Assert.Equal("No access", ex.Message);
        Assert.False(_nextCalled);
    }

    [Theory]
    [InlineData("Not authenticated")]
    [InlineData("No user identifier")]
    public async Task ForbiddenForAnAuthenticationFailure_ThrowsUnauthorized(string error)
    {
        var context = Context(new ObjectResult(Result<string>.Forbid(error)));

        await Assert.ThrowsAsync<UnauthorizedException>(() => _filter.OnResultExecutionAsync(context, NextAsync(context)));
    }

    [Fact]
    public async Task NotFoundResult_ThrowsNotFound()
    {
        var context = Context(new ObjectResult(Result<string>.NotFound("Gone")));

        await Assert.ThrowsAsync<NotFoundException>(() => _filter.OnResultExecutionAsync(context, NextAsync(context)));
    }

    [Fact]
    public async Task OtherObjectResults_AreLeftAlone()
    {
        var original = new ObjectResult(new { Name = "x" }) { StatusCode = 202 };
        var context = Context(original);

        await _filter.OnResultExecutionAsync(context, NextAsync(context));

        Assert.Same(original, context.Result);
        Assert.True(_nextCalled);
    }

    [Fact]
    public async Task NonObjectResults_AreLeftAlone()
    {
        var original = new NoContentResult();
        var context = Context(original);

        await _filter.OnResultExecutionAsync(context, NextAsync(context));

        Assert.Same(original, context.Result);
        Assert.True(_nextCalled);
    }

    private static ResultExecutingContext Context(IActionResult result) =>
        new(
            new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
            [],
            result,
            controller: new object());

    private ResultExecutionDelegate NextAsync(ResultExecutingContext context) => () =>
    {
        _nextCalled = true;
        return Task.FromResult(new ResultExecutedContext(context, context.Filters, context.Result, context.Controller));
    };
}
