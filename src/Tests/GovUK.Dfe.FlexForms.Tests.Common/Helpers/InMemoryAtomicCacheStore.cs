using System.Collections.Concurrent;
using System.Text;
using GovUK.Dfe.FlexForms.Domain.Caching;

namespace GovUK.Dfe.FlexForms.Tests.Common.Helpers;

/// <summary>
/// In-memory <see cref="IAtomicCacheStore"/> for integration tests, removing the dependency on a running Redis server.
/// </summary>
public sealed class InMemoryAtomicCacheStore : IAtomicCacheStore
{
    private readonly ConcurrentDictionary<string, (byte[] Value, DateTimeOffset ExpiresAt)> _items = new();

    public Task SetAsync(string key, byte[] value, TimeSpan expiry)
    {
        _items[key] = (value, DateTimeOffset.UtcNow + expiry);
        return Task.CompletedTask;
    }

    public Task<bool> SetIfAbsentAsync(string key, byte[] value, TimeSpan expiry)
    {
        RemoveIfExpired(key);
        return Task.FromResult(_items.TryAdd(key, (value, DateTimeOffset.UtcNow + expiry)));
    }

    public Task<byte[]?> GetAsync(string key)
    {
        RemoveIfExpired(key);
        return Task.FromResult(_items.TryGetValue(key, out var item) ? item.Value : null);
    }

    public Task<byte[]?> GetAndDeleteAsync(string key)
    {
        RemoveIfExpired(key);
        return Task.FromResult(_items.TryRemove(key, out var item) ? item.Value : null);
    }

    public Task<bool> DeleteAsync(string key) => Task.FromResult(_items.TryRemove(key, out _));

    public Task<bool> DeleteIfEqualsAsync(string key, byte[] expectedValue)
    {
        RemoveIfExpired(key);
        return Task.FromResult(
            _items.TryGetValue(key, out var item)
            && item.Value.AsSpan().SequenceEqual(expectedValue)
            && RemoveExact(key, item));
    }

    public Task<long> IncrementAsync(string key, TimeSpan expiry)
    {
        RemoveIfExpired(key);
        var updated = _items.AddOrUpdate(
            key,
            _ => (Encoding.UTF8.GetBytes("1"), DateTimeOffset.UtcNow + expiry),
            (_, item) => (Encoding.UTF8.GetBytes((long.Parse(Encoding.UTF8.GetString(item.Value)) + 1).ToString()), item.ExpiresAt));
        return Task.FromResult(long.Parse(Encoding.UTF8.GetString(updated.Value)));
    }

    private void RemoveIfExpired(string key)
    {
        if (_items.TryGetValue(key, out var item) && item.ExpiresAt <= DateTimeOffset.UtcNow)
            RemoveExact(key, item);
    }

    // Removes the key only while it still holds the entry that was looked at, so a concurrent replacement survives.
    private bool RemoveExact(string key, (byte[] Value, DateTimeOffset ExpiresAt) item)
        => ((ICollection<KeyValuePair<string, (byte[] Value, DateTimeOffset ExpiresAt)>>)_items).Remove(new(key, item));
}
