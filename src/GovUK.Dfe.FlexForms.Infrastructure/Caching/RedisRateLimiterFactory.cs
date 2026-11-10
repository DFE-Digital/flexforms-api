using System.Security.Cryptography;
using System.Text;
using GovUK.Dfe.CoreLibs.Utilities.RateLimiting;
using GovUK.Dfe.FlexForms.Utils.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace GovUK.Dfe.FlexForms.Infrastructure.Caching;

/// <summary>
/// Rate limiters whose counts are shared by every API replica, using a Redis sorted set as a sliding window.
/// The CoreLibs <see cref="RateLimitStore{TKey}"/> is per process, so with N replicas each limit became N times
/// larger and reset on every deployment. If Redis cannot be reached the in-process limiter is used instead,
/// so a Redis outage weakens the limit rather than lifting it.
/// </summary>
public sealed class RedisRateLimiterFactory(
    IServiceProvider serviceProvider,
    RateLimitStore<string> fallbackStore,
    TimeProvider timeProvider,
    ILogger<RedisRateLimiterFactory> logger) : IRateLimiterFactory<string>
{
    // Removes entries older than the window, then records the request only if the window has room.
    private const string SlidingWindowScript = """
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', ARGV[1] - ARGV[2])
        if redis.call('ZCARD', KEYS[1]) < tonumber(ARGV[3]) then
            redis.call('ZADD', KEYS[1], ARGV[1], ARGV[4])
            redis.call('PEXPIRE', KEYS[1], ARGV[2])
            return 1
        end
        return 0
        """;

    public IRateLimiter<string> Create(int maxRequests, TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRequests);
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));

        var fallback = new TimeBasedRateLimiter<string>(
            maxRequests,
            interval,
            fallbackStore,
            () => timeProvider.GetUtcNow().UtcDateTime);

        return new RedisRateLimiter(this, maxRequests, interval, fallback);
    }

    internal static RedisKey BuildKey(int maxRequests, TimeSpan interval, string key)
    {
        // Hashed so principal identifiers such as e-mail addresses never appear in Redis key names.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return $"{FlexFormsCacheKeys.RateLimitPrefix}{maxRequests}:{(long)interval.TotalMilliseconds}:{hash}";
    }

    private bool? TryAcquire(int maxRequests, TimeSpan interval, string key)
    {
        try
        {
            var redis = serviceProvider.GetRequiredService<IConnectionMultiplexer>();
            var now = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            var result = redis.GetDatabase().ScriptEvaluate(
                SlidingWindowScript,
                [BuildKey(maxRequests, interval, key)],
                [now, (long)interval.TotalMilliseconds, maxRequests, Guid.NewGuid().ToString("N")]);
            return (long)result == 1;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Shared rate limiter unavailable; falling back to this replica's own count");
            return null;
        }
    }

    private sealed class RedisRateLimiter(
        RedisRateLimiterFactory factory,
        int maxRequests,
        TimeSpan interval,
        IRateLimiter<string> fallback) : IRateLimiter<string>
    {
        public bool IsAllowed(string key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return factory.TryAcquire(maxRequests, interval, key) ?? fallback.IsAllowed(key);
        }
    }
}
