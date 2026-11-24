using GovUK.Dfe.FlexForms.Api.Security;
using GovUK.Dfe.FlexForms.Application.Common.Exceptions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Security;

public class AuthorizationFailureResponseHandlerTests
{
    private static readonly AuthorizationPolicy Policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

    private readonly AuthorizationFailureResponseHandler _handler = new();
    private readonly IAuthenticationService _authentication = Substitute.For<IAuthenticationService>();
    private bool _nextCalled;

    [Fact]
    public async Task Challenge_OnTheApi_ThrowsUnauthorized()
    {
        await Assert.ThrowsAsync<UnauthorizedException>(() => HandleAsync("/v1/applications", PolicyAuthorizationResult.Challenge()));
        Assert.False(_nextCalled);
    }

    [Fact]
    public async Task Forbid_OnTheApi_ThrowsForbidden()
    {
        await Assert.ThrowsAsync<AuthorizationForbiddenException>(() => HandleAsync("/v1/applications", PolicyAuthorizationResult.Forbid()));
        Assert.False(_nextCalled);
    }

    [Fact]
    public async Task Forbid_OnASignalRHub_StillThrowsForbidden()
    {
        await Assert.ThrowsAsync<AuthorizationForbiddenException>(() => HandleAsync("/hubs/notifications", PolicyAuthorizationResult.Forbid()));
    }

    [Theory]
    [InlineData("/hubs/notifications")]
    [InlineData("/HUBS/notifications/negotiate")]
    public async Task Challenge_OnASignalRHub_UsesTheNormalChallenge(string path)
    {
        var context = await HandleAsync(path, PolicyAuthorizationResult.Challenge());

        await _authentication.Received(1).ChallengeAsync(context, null, Arg.Any<AuthenticationProperties?>());
        Assert.False(_nextCalled);
    }

    [Fact]
    public async Task Challenge_OnAPathThatOnlyStartsWithHubs_ThrowsUnauthorized()
    {
        await Assert.ThrowsAsync<UnauthorizedException>(() => HandleAsync("/hubsx", PolicyAuthorizationResult.Challenge()));
    }

    [Fact]
    public async Task Success_CarriesOn()
    {
        await HandleAsync("/v1/applications", PolicyAuthorizationResult.Success());

        Assert.True(_nextCalled);
    }

    private async Task<HttpContext> HandleAsync(string path, PolicyAuthorizationResult result)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_authentication);
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Path = path;

        await _handler.HandleAsync(
            _ =>
            {
                _nextCalled = true;
                return Task.CompletedTask;
            },
            context,
            Policy,
            result);
        return context;
    }
}
