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

/// <summary>The cached OBO token: per user, per tenant, and never served once it is near expiry.</summary>
public class CachedInternalUserTokenStoreTests
{
    private const string Bob = "bob@example.com";
    private readonly IDistributedCache _cache = MemoryCache();

    [Fact]
    public async Task TokenSetInOneRequest_IsServedToTheNextRequestFromTheCache()
    {
        var token = Jwt(TimeSpan.FromHours(1));
        Store(SignedIn(Email(Bob))).SetToken(token);

        var next = SignedIn(Email(Bob));
        Assert.Equal(token, Store(next).GetToken());
        Assert.True(Store(SignedIn(Email(Bob))).IsTokenValid());
        Assert.NotNull(Store(SignedIn(Email(Bob))).GetTokenExpiry());
        Assert.Equal(token, next.Items["__InternalUserToken"]);
        Assert.NotNull(await _cache.GetStringAsync(CachedInternalUserTokenStore.BuildCacheKey(TenantA, Bob)));
    }

    [Fact]
    public void SameUserInAnotherTenant_DoesNotGetTheToken()
    {
        Store(SignedIn(Email(Bob)), TenantA).SetToken(Jwt(TimeSpan.FromHours(1)));

        Assert.Null(Store(SignedIn(Email(Bob)), TenantB).GetToken());
    }

    [Fact]
    public void AnotherUser_DoesNotGetTheToken()
    {
        Store(SignedIn(Email(Bob))).SetToken(Jwt(TimeSpan.FromHours(1)));

        Assert.Null(Store(SignedIn(Email("alice@example.com"))).GetToken());
    }

    [Fact]
    public void UsersSharingAnAppId_StillGetSeparateEntries()
    {
        Store(SignedIn(Email(Bob), new Claim("appid", "web-app"))).SetToken(Jwt(TimeSpan.FromHours(1)));

        Assert.Null(Store(SignedIn(Email("alice@example.com"), new Claim("appid", "web-app"))).GetToken());
    }

    [Theory]
    [InlineData("preferred_username")]
    [InlineData("email")]
    [InlineData("sub")]
    [InlineData(ClaimTypes.NameIdentifier)]
    [InlineData(ClaimTypes.Name)]
    public void UserIsKeyedByTheFirstIdentityClaimFound(string claimType)
    {
        var token = Jwt(TimeSpan.FromHours(1));
        Store(SignedIn(new Claim(claimType, "user-1"))).SetToken(token);

        Assert.Equal(token, Store(SignedIn(new Claim(claimType, "user-1"))).GetToken());
    }

    [Fact]
    public async Task TokenWithinFiveMinutesOfExpiry_IsDroppedFromTheCache()
    {
        Store(SignedIn(Email(Bob))).SetToken(Jwt(TimeSpan.FromMinutes(4)));

        Assert.Null(Store(SignedIn(Email(Bob))).GetToken());
        Assert.Null(await _cache.GetStringAsync(CachedInternalUserTokenStore.BuildCacheKey(TenantA, Bob)));
    }

    [Fact]
    public void StaleTokenInTheRequest_FallsBackToTheCache()
    {
        var fresh = Jwt(TimeSpan.FromHours(1));
        Store(SignedIn(Email(Bob))).SetToken(fresh);
        var context = SignedIn(Email(Bob));
        context.Items["__InternalUserToken"] = Jwt(TimeSpan.FromMinutes(1));

        Assert.Equal(fresh, Store(context).GetToken());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    public async Task UnreadableCacheEntry_IsRemoved(string cached)
    {
        var key = CachedInternalUserTokenStore.BuildCacheKey(TenantA, Bob);
        await _cache.SetStringAsync(key, cached);

        Assert.Null(Store(SignedIn(Email(Bob))).GetToken());
        Assert.Null(await _cache.GetStringAsync(key));
    }

    [Fact]
    public async Task ClearToken_RemovesItFromTheRequestAndTheCache()
    {
        var context = SignedIn(Email(Bob));
        var store = Store(context);
        store.SetToken(Jwt(TimeSpan.FromHours(1)));

        store.ClearToken();

        Assert.False(context.Items.ContainsKey("__InternalUserToken"));
        Assert.Null(await _cache.GetStringAsync(CachedInternalUserTokenStore.BuildCacheKey(TenantA, Bob)));
        Assert.False(Store(SignedIn(Email(Bob))).IsTokenValid());
        Assert.Null(Store(SignedIn(Email(Bob))).GetTokenExpiry());
    }

    [Fact]
    public void SignedOutUser_KeepsTheTokenForThisRequestOnly()
    {
        var token = Jwt(TimeSpan.FromHours(1));
        var context = SignedOut();
        var store = Store(context);

        store.SetToken(token);
        store.ClearToken();

        Assert.Null(Store(SignedOut()).GetToken());
    }

    [Fact]
    public void NoHttpContextOrEmptyToken_DoesNothing()
    {
        var noContext = Store(null);
        noContext.SetToken(Jwt(TimeSpan.FromHours(1)));
        noContext.ClearToken();
        Assert.Null(noContext.GetToken());

        var context = SignedIn(Email(Bob));
        Store(context).SetToken("");
        Assert.Empty(context.Items);
    }

    [Fact]
    public void CacheFailures_AreSwallowed()
    {
        var broken = Substitute.For<IDistributedCache>();
        broken.WhenForAnyArgs(c => c.Set(default!, default!, default!)).Throw(new InvalidOperationException("redis down"));
        broken.WhenForAnyArgs(c => c.Remove(default!)).Throw(new InvalidOperationException("redis down"));
        var store = new CachedInternalUserTokenStore(Accessor(SignedIn(Email(Bob))), broken, Settings(), NullLogger<CachedInternalUserTokenStore>.Instance);

        store.SetToken(Jwt(TimeSpan.FromHours(1)));
        store.ClearToken();
    }

    [Fact]
    public void NoTenantConfigured_RefusesToUseTheSharedCache()
    {
        var store = new CachedInternalUserTokenStore(
            Accessor(SignedIn(Email(Bob))), _cache, Settings(configure: s => s.TenantId = null), NullLogger<CachedInternalUserTokenStore>.Instance);

        Assert.Throws<InvalidOperationException>(() => store.SetToken(Jwt(TimeSpan.FromHours(1))));
    }

    [Theory]
    [InlineData(true, "t:11111111-1111-4111-8111-111111111111:FlexForms:InternalToken:bob")]
    [InlineData(false, "FlexForms:InternalToken:bob")]
    public void BuildCacheKey_PrefixesTheTenantWhenThereIsOne(bool withTenant, string expected)
    {
        Assert.Equal(expected, CachedInternalUserTokenStore.BuildCacheKey(withTenant ? TenantA : null, "bob"));
    }

    private CachedInternalUserTokenStore Store(HttpContext? context, Guid? tenantId = null) =>
        new(Accessor(context), _cache, Settings(tenantId), NullLogger<CachedInternalUserTokenStore>.Instance);

    private static Claim Email(string email) => new(ClaimTypes.Email, email);
}
