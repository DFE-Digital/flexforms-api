using GovUK.Dfe.FlexForms.Api.ExceptionHandlers;
using GovUK.Dfe.FlexForms.Application.Common.Exceptions;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.ExceptionHandlers;

public class ApplicationExceptionHandlerTests
{
    private readonly ApplicationExceptionHandler _handler = new();

    public static TheoryData<Exception, int, string> Mapped() => new()
    {
        { new BadRequestException("bad field"), 400, "Invalid request: bad field" },
        { new ForbiddenException("not yours"), 403, "not yours" },
        { new UnauthorizedException(), 401, "Unauthorized - no valid user token" },
        { new AuthorizationForbiddenException(), 403, "Forbidden - user does not have required permissions" },
        { new ConflictException("already exists"), 409, "Conflict error, already exists" },
        { new RateLimitExceededException("slow down"), 429, "TooManyRequests: slow down" },
        { new NotFoundException("no application"), 404, "Resource not found, no application" },
    };

    [Theory]
    [MemberData(nameof(Mapped))]
    public void ApplicationExceptions_MapToTheirStatusAndMessage(Exception exception, int status, string message)
    {
        Assert.True(_handler.CanHandle(exception.GetType()));

        var context = new Dictionary<string, object> { ["path"] = "/v1/x" };
        var response = _handler.Handle(exception, context);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(message, response.Message);
        Assert.Equal(exception.GetType().Name, response.ExceptionType);
        Assert.Same(context, response.Context);
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(NullReferenceException))]
    [InlineData(typeof(ValidationException))]
    public void OtherExceptions_AreLeftToOtherHandlers(Type type)
    {
        Assert.False(_handler.CanHandle(type));
    }

    [Fact]
    public void Priority_IsTwenty()
    {
        Assert.Equal(20, _handler.Priority);
    }

    // ArgumentException is accepted by CanHandle; its status mapping is not asserted here.
}
