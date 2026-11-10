namespace GovUK.Dfe.FlexForms.Domain.Caching;

/// <summary>
/// Single-key Redis operations that stay correct when several API replicas (or parallel requests) touch the same
/// key: use these instead of read-then-write on <c>IAdvancedRedisCacheService</c> for counters, one-time values and
/// cooldowns. Keys get the same <c>FlexForms:Cache:</c> prefix as the hybrid cache.
/// </summary>
public interface IAtomicCacheStore
{
    Task SetAsync(string key, byte[] value, TimeSpan expiry);

    /// <summary>Stores the value only if the key does not exist. Returns true when this caller stored it.</summary>
    Task<bool> SetIfAbsentAsync(string key, byte[] value, TimeSpan expiry);

    Task<byte[]?> GetAsync(string key);

    /// <summary>Returns the value and deletes the key in one step, so only one caller can ever receive it.</summary>
    Task<byte[]?> GetAndDeleteAsync(string key);

    /// <summary>Deletes the key. Returns true only for the caller that actually removed it.</summary>
    Task<bool> DeleteAsync(string key);

    /// <summary>Increments a counter, setting <paramref name="expiry"/> when the counter is created.</summary>
    Task<long> IncrementAsync(string key, TimeSpan expiry);
}
