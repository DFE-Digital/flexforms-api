using System.Security.Claims;
using GovUK.Dfe.FlexForms.Api.Client.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using static GovUK.Dfe.FlexForms.Api.Tests.ApiClient.Security.ApiClientTestKit;

namespace GovUK.Dfe.FlexForms.Api.Tests.ApiClient.Security;

public class TokenStateManagerTests
{
    private const string UserId = "bob@example.com";

    private readonly IDistributedCache _cache = MemoryCache();
    private readonly IInternalUserTokenStore _tokenStore = Substitute.For<IInternalUserTokenStore>();
    private readonly IAuthenticationSchemeStrategy _strategy = Substitute.For<IAuthenticationSchemeStrategy>();
    private HttpContext? _context = SignedIn(new Claim(ClaimTypes.Name, UserId));

    public TokenStateManagerTests()
    {
        _strategy.SchemeName.Returns("TestAuthentication");
        _strategy.GetUserId(Arg.Any<HttpContext>()).Returns(UserId);
        _strategy.GetExternalIdpTokenAsync(Arg.Any<HttpContext>()).Returns(new TokenInfo { Value = "idp", ExpiryTime = DateTime.UtcNow.AddHours(1) });
    }

    // ---- GetCurrentTokenStateAsync ----

    [Fact]
    public async Task SignedInUser_GetsBothTokensAndTheStrategysRefreshAnswer()
    {
        var expiry = DateTime.UtcNow.AddMinutes(30);
        _tokenStore.GetToken().Returns("obo");
        _tokenStore.GetTokenExpiry().Returns(expiry);
        _strategy.CanRefreshTokenAsync(Arg.Any<HttpContext>()).Returns(true);

        var state = await Manager().GetCurrentTokenStateAsync();

        Assert.True(state.IsAuthenticated);
        Assert.Equal("TestAuthentication", state.AuthenticationScheme);
        Assert.Equal(UserId, state.UserId);
        Assert.Equal("idp", state.ExternalIdpToken.Value);
        Assert.Equal("obo", state.OboToken.Value);
        Assert.Equal(expiry, state.OboToken.ExpiryTime);
        Assert.True(state.CanRefresh);
        Assert.Null(state.LogoutReason);
    }

    [Fact]
    public async Task SignedOutOrNoContext_IsNotAuthenticated()
    {
        _context = SignedOut();
        Assert.False((await Manager().GetCurrentTokenStateAsync()).IsAuthenticated);

        _context = null;
        Assert.False((await Manager().GetCurrentTokenStateAsync()).IsAuthenticated);
    }

    [Fact]
    public async Task UnknownScheme_HasNoStrategy()
    {
        _strategy.SchemeName.Returns("Cookies");

        var state = await Manager().GetCurrentTokenStateAsync();

        Assert.Equal("No authentication strategy for scheme: TestAuthentication", state.LogoutReason);
    }

    [Theory]
    [InlineData("TestAuthentication", "TestScheme")]
    [InlineData("MockScheme", "TestScheme")]
    [InlineData("OpenIdConnect", "OIDC")]
    [InlineData("OIDC-DfE", "OIDC")]
    [InlineData("Entra", "EntraSso")]
    public async Task SchemeNames_FallBackToTheMatchingStrategy(string authenticationType, string strategyScheme)
    {
        _context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, UserId)], authenticationType)) };
        _strategy.SchemeName.Returns(strategyScheme);

        var state = await Manager().GetCurrentTokenStateAsync();

        Assert.Equal(UserId, state.UserId);
    }

    [Fact]
    public async Task FederationLogin_UsesTheFirstRegisteredStrategy()
    {
        _context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, UserId)], "AuthenticationTypes.Federation")) };
        _strategy.SchemeName.Returns("DfESignIn");

        var state = await Manager().GetCurrentTokenStateAsync();

        Assert.Equal("DfESignIn", state.AuthenticationScheme);
        Assert.Equal(UserId, state.UserId);
    }

    [Fact]
    public async Task NoUserId_IsReported()
    {
        _strategy.GetUserId(Arg.Any<HttpContext>()).Returns((string?)null);

        Assert.Equal("No user ID found", (await Manager().GetCurrentTokenStateAsync()).LogoutReason);
    }

    [Fact]
    public async Task LogoutFlagWithAFreshLogin_IsClearedAndTheOboTokenDropped()
    {
        await CacheManager().SetLogoutFlagAsync(UserId, TimeSpan.FromMinutes(10));
        _context = SignedIn(new Claim(ClaimTypes.Name, UserId));
        _strategy.GetExternalIdpTokenAsync(Arg.Any<HttpContext>())
            .Returns(new TokenInfo { Value = Jwt(TimeSpan.FromHours(1)), ExpiryTime = DateTime.UtcNow.AddHours(1) });

        var state = await Manager().GetCurrentTokenStateAsync();

        Assert.True(state.CanRefresh);
        Assert.Null(state.LogoutReason);
        Assert.False(await CacheManager().IsLogoutFlagSetAsync(UserId));
        Assert.True(_context.Items.ContainsKey("ReAuthenticationDetected"));
        _tokenStore.Received(1).ClearToken();
    }

    [Fact]
    public async Task LogoutFlagWithoutAnIdpToken_StaysLoggedOut()
    {
        await CacheManager().SetLogoutFlagAsync(UserId, TimeSpan.FromMinutes(10));
        _context = SignedIn(new Claim(ClaimTypes.Name, UserId));
        _strategy.GetExternalIdpTokenAsync(Arg.Any<HttpContext>()).Returns(new TokenInfo());

        var state = await Manager().GetCurrentTokenStateAsync();

        Assert.Equal("Logout flag set (no External IDP token)", state.LogoutReason);
        Assert.True(await CacheManager(new DefaultHttpContext()).IsLogoutFlagSetAsync(UserId));
    }

    [Fact]
    public async Task LogoutFlagWithAnUnreadableIdpToken_StaysLoggedOut()
    {
        await CacheManager().SetLogoutFlagAsync(UserId, TimeSpan.FromMinutes(10));
        _strategy.GetExternalIdpTokenAsync(Arg.Any<HttpContext>()).Returns(new TokenInfo { Value = "not-a-jwt" });

        Assert.Equal("Logout flag set (token check error)", (await Manager().GetCurrentTokenStateAsync()).LogoutReason);
    }

    [Fact]
    public async Task FiveMinutesInactive_AllowsARefresh()
    {
        await CacheManager().SetLastActivityAsync(UserId, DateTime.UtcNow.AddMinutes(-6));

        var state = await Manager().GetCurrentTokenStateAsync();

        Assert.True(state.CanRefresh);
        Assert.True(_context!.Items.ContainsKey("AllowRefreshDueToInactivity"));
    }

    [Fact]
    public async Task RecentActivity_LeavesTheRefreshDecisionToTheStrategy()
    {
        await CacheManager().SetLastActivityAsync(UserId, DateTime.UtcNow.AddMinutes(-1));

        Assert.False((await Manager().GetCurrentTokenStateAsync()).CanRefresh);
    }

    [Theory]
    [InlineData(true, false, "External IDP token expired and cannot refresh")]
    [InlineData(false, false, "OBO token expired and External IDP cannot refresh")]
    [InlineData(true, true, "Tokens expired but refresh possible")]
    public async Task ExpiredTokens_ExplainWhy(bool idpExpired, bool canRefresh, string reason)
    {
        _strategy.GetExternalIdpTokenAsync(Arg.Any<HttpContext>())
            .Returns(new TokenInfo { Value = "idp", ExpiryTime = DateTime.UtcNow.AddMinutes(idpExpired ? 1 : 60) });
        _tokenStore.GetToken().Returns("obo");
        _tokenStore.GetTokenExpiry().Returns(DateTime.UtcNow.AddMinutes(idpExpired ? 60 : 1));
        _strategy.CanRefreshTokenAsync(Arg.Any<HttpContext>()).Returns(canRefresh);

        var manager = Manager();
        var state = await manager.GetCurrentTokenStateAsync();

        Assert.Equal(reason, state.LogoutReason);
        Assert.Equal(!canRefresh, manager.ShouldForceLogout(state));
    }

    [Fact]
    public async Task OboStoreFailure_IsTreatedAsNoOboToken()
    {
        _tokenStore.GetToken().Throws(new InvalidOperationException("cache down"));

        Assert.False((await Manager().GetCurrentTokenStateAsync()).OboToken.IsPresent);
    }

    [Fact]
    public void ShouldForceLogout_NeverForAnUnauthenticatedState()
    {
        Assert.False(Manager().ShouldForceLogout(new TokenState { IsAuthenticated = false, OboToken = new TokenInfo { ExpiryTime = DateTime.UtcNow } }));
    }

    // ---- ForceCompleteLogoutAsync ----

    [Fact]
    public async Task ForceLogout_FlagsTheUserForTenMinutesAndClearsTheirTokens()
    {
        var cache = Substitute.For<ICacheManager>();

        Assert.True(await Manager(cache).ForceCompleteLogoutAsync());

        await cache.Received(1).SetLogoutFlagAsync(UserId, TimeSpan.FromMinutes(10));
        await cache.Received(1).ClearAllTokenCachesAsync(UserId);
    }

    [Fact]
    public async Task ForceLogout_WithoutAUserOrWhenTheCacheFails_ReturnsFalse()
    {
        var cache = Substitute.For<ICacheManager>();
        cache.SetLogoutFlagAsync(default!, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("redis down"));
        Assert.False(await Manager(cache).ForceCompleteLogoutAsync());

        _strategy.GetUserId(Arg.Any<HttpContext>()).Returns((string?)null);
        Assert.False(await Manager().ForceCompleteLogoutAsync());

        _context = null;
        Assert.False(await Manager().ForceCompleteLogoutAsync());
    }

    // ---- RefreshTokensIfPossibleAsync ----

    [Fact]
    public async Task Refresh_SuccessClearsTheOboTokenAndTheLogoutFlag()
    {
        var cache = Substitute.For<ICacheManager>();
        _strategy.CanRefreshTokenAsync(Arg.Any<HttpContext>()).Returns(true);
        _strategy.RefreshExternalIdpTokenAsync(Arg.Any<HttpContext>()).Returns(true);

        Assert.True(await Manager(cache).RefreshTokensIfPossibleAsync());

        _tokenStore.Received(1).ClearToken();
        await cache.Received(1).ClearLogoutFlagAsync(UserId);
        cache.Received(1).SetRequestScopedFlag("ReAuthenticationDetected", true);
        await cache.Received(1).SetLastActivityAsync(UserId, Arg.Any<DateTime>(), TimeSpan.FromHours(2));
    }

    [Fact]
    public async Task Refresh_InactivityFlagSkipsTheStrategysRefreshWindow()
    {
        var cache = Substitute.For<ICacheManager>();
        cache.HasRequestScopedFlag("AllowRefreshDueToInactivity").Returns(true);
        _strategy.RefreshExternalIdpTokenAsync(Arg.Any<HttpContext>()).Returns(true);

        Assert.True(await Manager(cache).RefreshTokensIfPossibleAsync());
        await _strategy.DidNotReceive().CanRefreshTokenAsync(Arg.Any<HttpContext>());
    }

    [Fact]
    public async Task Refresh_NotAllowedFailedOrThrowing_ReturnsFalse()
    {
        Assert.False(await Manager().RefreshTokensIfPossibleAsync());

        _strategy.CanRefreshTokenAsync(Arg.Any<HttpContext>()).Returns(true);
        Assert.False(await Manager().RefreshTokensIfPossibleAsync());

        _strategy.RefreshExternalIdpTokenAsync(Arg.Any<HttpContext>()).ThrowsAsync(new InvalidOperationException("idp down"));
        Assert.False(await Manager().RefreshTokensIfPossibleAsync());

        _tokenStore.DidNotReceive().ClearToken();
    }

    [Fact]
    public async Task Refresh_WithoutAContextStrategyOrUser_ReturnsFalse()
    {
        _strategy.GetUserId(Arg.Any<HttpContext>()).Returns((string?)null);
        Assert.False(await Manager().RefreshTokensIfPossibleAsync());

        _strategy.SchemeName.Returns("Cookies");
        Assert.False(await Manager().RefreshTokensIfPossibleAsync());

        _context = null;
        Assert.False(await Manager().RefreshTokensIfPossibleAsync());
    }

    [Fact]
    public void IsLogoutRequired_ReadsTheRequestFlag()
    {
        var cache = Substitute.For<ICacheManager>();
        cache.HasRequestScopedFlag("RequireLogout").Returns(true);

        Assert.True(Manager(cache).IsLogoutRequired());
        Assert.False(Manager().IsLogoutRequired());
    }

    private DistributedCacheManager CacheManager(HttpContext? context = null) =>
        new(_cache, Accessor(context ?? _context), _tokenStore, Settings(), NullLogger<DistributedCacheManager>.Instance);

    private TokenStateManager Manager(ICacheManager? cache = null) =>
        new(Accessor(_context), _tokenStore, cache ?? CacheManager(), [_strategy], NullLogger<TokenStateManager>.Instance);
}
