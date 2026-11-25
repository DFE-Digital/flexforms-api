using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Json;
using GovUK.Dfe.FlexForms.Api.Client.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.ApiClient.Security;

public class TokenManagementMiddlewareTests
{
    private const string UserName = "bob@example.com";

    private readonly ITokenStateManager _state = Substitute.For<ITokenStateManager>();
    private readonly ICacheManager _cache = Substitute.For<ICacheManager>();
    private readonly IAuthenticationService _authentication = Substitute.For<IAuthenticationService>();
    private readonly IAuthenticationSchemeProvider _schemes = Substitute.For<IAuthenticationSchemeProvider>();
    private readonly TestSession _session = new();
    private bool _nextCalled;

    [Fact]
    public async Task SignedOutRequest_IsPassedStraightThrough()
    {
        var context = Context(authenticationType: null);

        await InvokeAsync(context);

        Assert.True(_nextCalled);
        await _state.DidNotReceive().GetCurrentTokenStateAsync();
    }

    [Theory]
    [InlineData("/TestLogin")]
    [InlineData("/testlogout/confirm")]
    [InlineData("/Logout")]
    public async Task SignInAndOutPages_SkipTokenManagement(string path)
    {
        await InvokeAsync(Context(path: path));

        Assert.True(_nextCalled);
        await _state.DidNotReceive().GetCurrentTokenStateAsync();
    }

    [Theory]
    [InlineData("/applications", "application/json", null)]
    [InlineData("/api/notifications", "text/html", null)]
    [InlineData("/applications", "text/html", "XMLHttpRequest")]
    public async Task ForcedLogoutOnAnApiRequest_Answers401Json(string path, string accept, string? requestedWith)
    {
        ForceLogout("OBO token expired and External IDP cannot refresh");
        var context = Context(path: path);
        context.Request.Headers.Accept = accept;
        if (requestedWith is not null)
            context.Request.Headers["X-Requested-With"] = requestedWith;

        await InvokeAsync(context);

        Assert.False(_nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("unauthorized", body.RootElement.GetProperty("error").GetString());
        Assert.Equal("OBO token expired and External IDP cannot refresh", body.RootElement.GetProperty("reason").GetString());
        await _state.Received(1).ForceCompleteLogoutAsync();
    }

    [Fact]
    public async Task ForcedLogoutWithTestAuthentication_SignsOutClearsTheTestSessionAndRedirectsHome()
    {
        ForceLogout(reason: null);
        _session.SetString("TestAuth:Email", UserName);
        _session.SetString("TestAuth:Token", "t");
        _session.SetString("Other", "kept");
        var context = Context(authenticationType: "TestAuthentication");

        await InvokeAsync(context);

        Assert.False(_nextCalled);
        Assert.Equal("/", context.Response.Headers.Location.ToString());
        await _authentication.Received(1).SignOutAsync(context, null, Arg.Any<AuthenticationProperties?>());
        Assert.Equal(["Other"], _session.Keys);
    }

    [Fact]
    public async Task ForcedLogoutWithOidc_SignsOutOfTheCookieAndTheIdpAndClearsTheSession()
    {
        ForceLogout(reason: null);
        _schemes.GetDefaultSignOutSchemeAsync().Returns(new AuthenticationScheme("DfESignIn", null, typeof(CookieAuthenticationHandler)));
        _session.SetString("Anything", "x");
        var context = Context(authenticationType: "AuthenticationTypes.Federation");

        await InvokeAsync(context);

        Assert.Equal("/", context.Response.Headers.Location.ToString());
        await _authentication.Received(1).SignOutAsync(context, CookieAuthenticationDefaults.AuthenticationScheme, Arg.Any<AuthenticationProperties?>());
        await _authentication.Received(1).SignOutAsync(context, "DfESignIn", Arg.Any<AuthenticationProperties?>());
        Assert.Empty(_session.Keys);
    }

    [Fact]
    public async Task ExpiredButRefreshable_IsRefreshedAndCarriesOn()
    {
        _state.GetCurrentTokenStateAsync().Returns(new TokenState
        {
            IsAuthenticated = true,
            CanRefresh = true,
            OboToken = new TokenInfo { Value = "obo", ExpiryTime = DateTime.UtcNow }
        });

        await InvokeAsync(Context());

        await _state.Received(1).RefreshTokensIfPossibleAsync();
        Assert.True(_nextCalled);
    }

    [Fact]
    public async Task InactivityFlag_TriggersARefreshEvenWithValidTokens()
    {
        _state.GetCurrentTokenStateAsync().Returns(new TokenState { IsAuthenticated = true });
        _cache.HasRequestScopedFlag("AllowRefreshDueToInactivity").Returns(true);

        await InvokeAsync(Context());

        await _state.Received(1).RefreshTokensIfPossibleAsync();
    }

    [Fact]
    public async Task ValidTokens_AreLeftAloneAndActivityIsRecorded()
    {
        _state.GetCurrentTokenStateAsync().Returns(new TokenState { IsAuthenticated = true });

        await InvokeAsync(Context());

        Assert.True(_nextCalled);
        await _state.DidNotReceive().RefreshTokensIfPossibleAsync();
        await _cache.Received(1).SetLastActivityAsync(UserName, Arg.Any<DateTime>(), TimeSpan.FromHours(2));
    }

    [Fact]
    public async Task TokenManagementErrors_DoNotBreakTheRequest()
    {
        _state.GetCurrentTokenStateAsync().ThrowsAsync(new InvalidOperationException("cache down"));
        _cache.SetLastActivityAsync(default!, default, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("cache down"));

        await InvokeAsync(Context());

        Assert.True(_nextCalled);
    }

    private void ForceLogout(string? reason)
    {
        var state = new TokenState { IsAuthenticated = true, LogoutReason = reason };
        _state.GetCurrentTokenStateAsync().Returns(state);
        _state.ShouldForceLogout(state).Returns(true);
    }

    private DefaultHttpContext Context(string path = "/applications/dashboard", string? authenticationType = "TestAuthentication")
    {
        var services = new ServiceCollection();
        services.AddSingleton(_cache);
        services.AddSingleton(_authentication);
        services.AddSingleton(_schemes);
        var identity = authenticationType is null
            ? new ClaimsIdentity()
            : new ClaimsIdentity([new Claim(ClaimTypes.Name, UserName)], authenticationType);
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity), RequestServices = services.BuildServiceProvider() };
        context.Features.Set<ISessionFeature>(new SessionFeature { Session = _session });
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private Task InvokeAsync(HttpContext context) =>
        new TokenManagementMiddleware(
                _ =>
                {
                    _nextCalled = true;
                    return Task.CompletedTask;
                },
                NullLogger<TokenManagementMiddleware>.Instance)
            .InvokeAsync(context, _state);

    private sealed class SessionFeature : ISessionFeature
    {
        public ISession Session { get; set; } = null!;
    }

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = [];

        public bool IsAvailable => true;
        public string Id => "session-1";
        public IEnumerable<string> Keys => _values.Keys;

        public void Clear() => _values.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _values.Remove(key);
        public void Set(string key, byte[] value) => _values[key] = value;
        public bool TryGetValue(string key, [NotNullWhen(true)] out byte[]? value) => _values.TryGetValue(key, out value);
    }
}
