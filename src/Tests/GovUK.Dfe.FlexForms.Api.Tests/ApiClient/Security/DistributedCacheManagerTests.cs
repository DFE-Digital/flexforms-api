using GovUK.Dfe.FlexForms.Api.Client.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using static GovUK.Dfe.FlexForms.Api.Tests.ApiClient.Security.ApiClientTestKit;

namespace GovUK.Dfe.FlexForms.Api.Tests.ApiClient.Security;

/// <summary>Logout flags and last-activity times: scoped to the tenant as well as the user.</summary>
public class DistributedCacheManagerTests
{
    private const string Bob = "bob@example.com";
    private readonly IDistributedCache _cache = MemoryCache();
    private readonly IInternalUserTokenStore _tokenStore = Substitute.For<IInternalUserTokenStore>();
    private readonly DefaultHttpContext _context = new();

    [Fact]
    public async Task LogoutFlag_IsSeenByLaterRequestsInTheSameTenantOnly()
    {
        await Manager().SetLogoutFlagAsync(Bob, TimeSpan.FromMinutes(10));

        Assert.True(_context.Items.ContainsKey("RequireLogout"));
        Assert.True(await Manager(new DefaultHttpContext()).IsLogoutFlagSetAsync(Bob));
        Assert.False(await Manager(new DefaultHttpContext(), TenantB).IsLogoutFlagSetAsync(Bob));
        Assert.False(await Manager(new DefaultHttpContext()).IsLogoutFlagSetAsync("alice@example.com"));
    }

    [Fact]
    public async Task ClearLogoutFlag_RemovesItFromTheCacheAndTheRequest()
    {
        var manager = Manager();
        await manager.SetLogoutFlagAsync(Bob, TimeSpan.FromMinutes(10));

        await manager.ClearLogoutFlagAsync(Bob);

        Assert.False(_context.Items.ContainsKey("RequireLogout"));
        Assert.False(await Manager(new DefaultHttpContext()).IsLogoutFlagSetAsync(Bob));
    }

    [Fact]
    public async Task ClearAllTokenCaches_ClearsTheStoreTheUsersEntriesAndTokenItems()
    {
        foreach (var name in new[] { "obo_token_", "token_expiry_", "user_tokens_" })
            await _cache.SetStringAsync(TenantPrefixedCacheKey.Create(TenantA, name + Bob), "x");
        await _cache.SetStringAsync(TenantPrefixedCacheKey.Create(TenantB, "obo_token_" + Bob), "other tenant");
        _context.Items["__InternalUserToken"] = "t";
        _context.Items["IdToken"] = "t";
        _context.Items["unrelated"] = "keep";

        await Manager().ClearAllTokenCachesAsync(Bob);

        _tokenStore.Received(1).ClearToken();
        foreach (var name in new[] { "obo_token_", "token_expiry_", "user_tokens_" })
            Assert.Null(await _cache.GetStringAsync(TenantPrefixedCacheKey.Create(TenantA, name + Bob)));
        Assert.Equal("other tenant", await _cache.GetStringAsync(TenantPrefixedCacheKey.Create(TenantB, "obo_token_" + Bob)));
        Assert.Equal(["unrelated"], _context.Items.Keys.Select(k => k.ToString()));
    }

    [Fact]
    public async Task LastActivity_RoundTripsAsUtcPerTenant()
    {
        var local = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Local);

        await Manager().SetLastActivityAsync(Bob, local, TimeSpan.FromHours(2));

        Assert.Equal(local.ToUniversalTime(), await Manager().GetLastActivityAsync(Bob));
        Assert.Equal(DateTimeKind.Utc, (await Manager().GetLastActivityAsync(Bob))!.Value.Kind);
        Assert.Null(await Manager(tenantId: TenantB).GetLastActivityAsync(Bob));
    }

    [Theory]
    [InlineData("2026-06-01 10:00:00", true)]
    [InlineData("not a date", false)]
    public async Task LastActivity_ReadsOlderFormatsAndIgnoresJunk(string stored, bool parses)
    {
        await _cache.SetStringAsync(TenantPrefixedCacheKey.Create(TenantA, "last_activity_" + Bob), stored);

        var value = await Manager().GetLastActivityAsync(Bob);

        Assert.Equal(parses ? new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc) : null, value);
    }

    [Fact]
    public void RequestScopedFlags_LiveOnTheCurrentRequest()
    {
        var manager = Manager();

        manager.SetRequestScopedFlag("AllowRefreshDueToInactivity", true);

        Assert.True(manager.HasRequestScopedFlag("AllowRefreshDueToInactivity"));
        Assert.True(manager.GetRequestScopedFlag<bool>("AllowRefreshDueToInactivity"));
        Assert.Null(manager.GetRequestScopedFlag<string>("AllowRefreshDueToInactivity"));
        Assert.False(manager.HasRequestScopedFlag("Other"));
    }

    [Fact]
    public async Task WithoutAnHttpContext_RequestFlagsAreIgnored()
    {
        var manager = new DistributedCacheManager(_cache, Accessor(null), _tokenStore, Settings(), NullLogger<DistributedCacheManager>.Instance);

        manager.SetRequestScopedFlag("RequireLogout", true);

        Assert.False(manager.HasRequestScopedFlag("RequireLogout"));
        Assert.False(await manager.IsLogoutFlagSetAsync(Bob));
    }

    [Fact]
    public async Task CacheFailures_AreReadAsUnsetButWritesStillThrow()
    {
        var broken = Substitute.For<IDistributedCache>();
        broken.GetAsync(default!, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("redis down"));
        broken.SetAsync(default!, default!, default!, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("redis down"));
        broken.RemoveAsync(default!, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("redis down"));
        var manager = new DistributedCacheManager(broken, Accessor(_context), _tokenStore, Settings(), NullLogger<DistributedCacheManager>.Instance);

        Assert.False(await manager.IsLogoutFlagSetAsync(Bob));
        Assert.Null(await manager.GetLastActivityAsync(Bob));
        await manager.SetLastActivityAsync(Bob, DateTime.UtcNow);
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.SetLogoutFlagAsync(Bob, TimeSpan.FromMinutes(1)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ClearLogoutFlagAsync(Bob));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ClearAllTokenCachesAsync(Bob));
    }

    private DistributedCacheManager Manager(HttpContext? context = null, Guid? tenantId = null) =>
        new(_cache, Accessor(context ?? _context), _tokenStore, Settings(tenantId), NullLogger<DistributedCacheManager>.Instance);
}
