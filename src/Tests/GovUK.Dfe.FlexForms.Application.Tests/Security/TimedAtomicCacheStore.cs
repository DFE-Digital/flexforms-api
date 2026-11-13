using System.Text;
using GovUK.Dfe.FlexForms.Domain.Caching;

namespace GovUK.Dfe.FlexForms.Application.Tests.Security;

/// <summary>Clock a test moves forward by hand.</summary>
internal sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>
/// <see cref="IAtomicCacheStore"/> double whose entries expire against a test-controlled clock. Each operation is
/// atomic, like the Redis one. <see cref="HoldReadsUntil"/> keeps readers waiting until that many have read, so
/// parallel requests really act on the same value.
/// </summary>
internal sealed class TimedAtomicCacheStore(TimeProvider timeProvider) : IAtomicCacheStore
{
    private readonly Dictionary<string, (byte[] Value, DateTimeOffset ExpiresAt, TimeSpan Expiry)> _items = new();
    private readonly Lock _sync = new();
    private TaskCompletionSource? _readsReleased;
    private int _readsToHold;
    private int _reads;

    public void HoldReadsUntil(int readers)
    {
        _readsToHold = readers;
        _reads = 0;
        _readsReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public IReadOnlyList<KeyValuePair<string, (byte[] Value, TimeSpan Expiry)>> Entries
    {
        get
        {
            lock (_sync)
            {
                RemoveExpired();
                return _items
                    .Select(item => KeyValuePair.Create(item.Key, (item.Value.Value, item.Value.Expiry)))
                    .ToList();
            }
        }
    }

    public Task SetAsync(string key, byte[] value, TimeSpan expiry)
    {
        lock (_sync)
            _items[key] = (value, timeProvider.GetUtcNow() + expiry, expiry);
        return Task.CompletedTask;
    }

    public Task<bool> SetIfAbsentAsync(string key, byte[] value, TimeSpan expiry)
    {
        lock (_sync)
        {
            RemoveExpired();
            if (_items.ContainsKey(key))
                return Task.FromResult(false);

            _items[key] = (value, timeProvider.GetUtcNow() + expiry, expiry);
            return Task.FromResult(true);
        }
    }

    public async Task<byte[]?> GetAsync(string key)
    {
        byte[]? value;
        lock (_sync)
        {
            RemoveExpired();
            value = _items.TryGetValue(key, out var item) ? item.Value : null;
        }

        if (_readsReleased is { } released)
        {
            if (Interlocked.Increment(ref _reads) >= _readsToHold)
                released.TrySetResult();
            await released.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        return value;
    }

    public Task<byte[]?> GetAndDeleteAsync(string key)
    {
        lock (_sync)
        {
            RemoveExpired();
            return Task.FromResult(_items.Remove(key, out var item) ? item.Value : null);
        }
    }

    public Task<bool> DeleteAsync(string key)
    {
        lock (_sync)
            return Task.FromResult(_items.Remove(key));
    }

    public Task<bool> DeleteIfEqualsAsync(string key, byte[] expectedValue)
    {
        lock (_sync)
        {
            RemoveExpired();
            return Task.FromResult(
                _items.TryGetValue(key, out var item)
                && item.Value.AsSpan().SequenceEqual(expectedValue)
                && _items.Remove(key));
        }
    }

    public Task<long> IncrementAsync(string key, TimeSpan expiry)
    {
        lock (_sync)
        {
            RemoveExpired();
            if (_items.TryGetValue(key, out var item))
            {
                var count = long.Parse(Encoding.UTF8.GetString(item.Value)) + 1;
                _items[key] = (Encoding.UTF8.GetBytes(count.ToString()), item.ExpiresAt, item.Expiry);
                return Task.FromResult(count);
            }

            _items[key] = (Encoding.UTF8.GetBytes("1"), timeProvider.GetUtcNow() + expiry, expiry);
            return Task.FromResult(1L);
        }
    }

    private void RemoveExpired()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var key in _items.Where(item => item.Value.ExpiresAt <= now).Select(item => item.Key).ToList())
            _items.Remove(key);
    }
}
