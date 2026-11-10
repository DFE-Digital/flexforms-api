using GovUK.Dfe.CoreLibs.Caching.Settings;
using GovUK.Dfe.FlexForms.Domain.Caching;
using GovUK.Dfe.FlexForms.Utils.Caching;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace GovUK.Dfe.FlexForms.Infrastructure.Caching;

/// <inheritdoc />
public sealed class RedisAtomicCacheStore(
    IConnectionMultiplexer redis,
    IOptions<CacheSettings> cacheSettings) : IAtomicCacheStore
{
    // Lua rather than GETDEL so it also works on Redis versions before 6.2.
    private const string GetAndDeleteScript = """
        local value = redis.call('GET', KEYS[1])
        if value then redis.call('DEL', KEYS[1]) end
        return value
        """;

    private const string DeleteIfEqualsScript = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('DEL', KEYS[1])
        end
        return 0
        """;

    private const string IncrementScript = """
        local count = redis.call('INCR', KEYS[1])
        if count == 1 then redis.call('PEXPIRE', KEYS[1], ARGV[1]) end
        return count
        """;

    private IDatabase Database => redis.GetDatabase(cacheSettings.Value.Redis?.Database ?? -1);

    public Task SetAsync(string key, byte[] value, TimeSpan expiry)
        => Database.StringSetAsync(FullKey(key), value, expiry);

    public Task<bool> SetIfAbsentAsync(string key, byte[] value, TimeSpan expiry)
        => Database.StringSetAsync(FullKey(key), value, expiry, When.NotExists);

    public async Task<byte[]?> GetAsync(string key)
        => await Database.StringGetAsync(FullKey(key)) is { IsNull: false } value ? (byte[])value! : null;

    public async Task<byte[]?> GetAndDeleteAsync(string key)
    {
        var result = await Database.ScriptEvaluateAsync(GetAndDeleteScript, [FullKey(key)]);
        return result.IsNull ? null : (byte[])result!;
    }

    public Task<bool> DeleteAsync(string key) => Database.KeyDeleteAsync(FullKey(key));

    public async Task<bool> DeleteIfEqualsAsync(string key, byte[] expectedValue)
    {
        var result = await Database.ScriptEvaluateAsync(DeleteIfEqualsScript, [FullKey(key)], [expectedValue]);
        return (long)result == 1;
    }

    public async Task<long> IncrementAsync(string key, TimeSpan expiry)
    {
        var result = await Database.ScriptEvaluateAsync(
            IncrementScript,
            [FullKey(key)],
            [(long)expiry.TotalMilliseconds]);
        return (long)result;
    }

    private static RedisKey FullKey(string key) => $"{FlexFormsCacheKeys.RedisKeyPrefix}{key}";
}
