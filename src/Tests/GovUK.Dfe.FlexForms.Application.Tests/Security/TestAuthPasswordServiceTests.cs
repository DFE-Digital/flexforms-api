using System.Text;
using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Domain.Caching;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Security;

public class TestAuthPasswordServiceTests
{
    private const string Email = "tester@education.gov.uk";

    private readonly ManualTimeProvider _timeProvider = new(new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero));
    private readonly InMemoryAtomicStore _store;
    private readonly ITenantContextAccessor _tenantContextAccessor = Substitute.For<ITenantContextAccessor>();
    private readonly Guid _tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private readonly TestAuthPasswordService _service;

    public TestAuthPasswordServiceTests()
    {
        _store = new InMemoryAtomicStore(_timeProvider);
        _tenantContextAccessor.CurrentTenant.Returns(
            new TenantConfiguration(_tenantId, "TestTenant", new ConfigurationBuilder().Build(), []));

        _service = new TestAuthPasswordService(
            _store,
            _tenantContextAccessor,
            _timeProvider,
            NullLogger<TestAuthPasswordService>.Instance);
    }

    [Fact]
    public async Task IssueAsync_returns_random_six_digit_password()
    {
        var password = await _service.IssueAsync(Email);

        Assert.NotNull(password);
        Assert.Matches(@"^\d{6}$", password);
    }

    [Fact]
    public async Task IssueAsync_stores_password_in_redis_with_one_hour_expiry()
    {
        await _service.IssueAsync(Email);

        var (key, entry) = Assert.Single(PasswordEntries());
        Assert.Equal(TestAuthPasswordService.PasswordLifetime, entry.Expiry);
        Assert.StartsWith($"t:{_tenantId}:TestAuthPassword_", key);
    }

    [Fact]
    public async Task IssueAsync_does_not_store_plaintext_password_or_email()
    {
        var password = await _service.IssueAsync(Email);

        var (key, entry) = Assert.Single(PasswordEntries());
        var stored = Encoding.UTF8.GetString(entry.Value);
        Assert.DoesNotContain(password!, stored);
        Assert.DoesNotContain(Email, key, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IssueAsync_uses_same_key_regardless_of_email_case_and_whitespace()
    {
        await _service.IssueAsync(Email);
        _timeProvider.Advance(TestAuthPasswordService.ResendCooldown);
        await _service.IssueAsync($"  {Email.ToUpperInvariant()} ");

        Assert.Single(PasswordEntries());
    }

    [Fact]
    public async Task IssueAsync_scopes_key_to_tenant()
    {
        await _service.IssueAsync(Email);

        var otherTenantId = Guid.NewGuid();
        _tenantContextAccessor.CurrentTenant.Returns(
            new TenantConfiguration(otherTenantId, "OtherTenant", new ConfigurationBuilder().Build(), []));
        await _service.IssueAsync(Email);

        Assert.Equal(2, PasswordEntries().Count);
        Assert.Contains(PasswordEntries(), entry => entry.Key.StartsWith($"t:{otherTenantId}:"));
    }

    [Fact]
    public async Task IssueAsync_returns_null_and_keeps_existing_password_within_resend_cooldown()
    {
        var first = await _service.IssueAsync(Email);
        _timeProvider.Advance(TestAuthPasswordService.ResendCooldown - TimeSpan.FromSeconds(1));

        var second = await _service.IssueAsync(Email);

        Assert.Null(second);
        Assert.True(await _service.VerifyAsync(Email, first!));
    }

    [Fact]
    public async Task IssueAsync_issues_only_one_password_when_requests_arrive_together()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => _service.IssueAsync(Email)));

        Assert.Single(results, password => password is not null);
    }

    [Fact]
    public async Task IssueAsync_replaces_existing_password_after_resend_cooldown()
    {
        var first = await _service.IssueAsync(Email);
        _timeProvider.Advance(TestAuthPasswordService.ResendCooldown);

        var second = await _service.IssueAsync(Email);

        Assert.NotNull(second);
        if (first != second)
        {
            Assert.False(await _service.VerifyAsync(Email, first!));
        }
        Assert.True(await _service.VerifyAsync(Email, second!));
    }

    [Fact]
    public async Task IssueAsync_resets_failed_attempts()
    {
        var first = await _service.IssueAsync(Email);
        for (var i = 0; i < TestAuthPasswordService.MaxFailedAttempts - 1; i++)
        {
            await _service.VerifyAsync(Email, WrongPassword(first!));
        }

        _timeProvider.Advance(TestAuthPasswordService.ResendCooldown);
        var second = await _service.IssueAsync(Email);
        for (var i = 0; i < TestAuthPasswordService.MaxFailedAttempts - 1; i++)
        {
            await _service.VerifyAsync(Email, WrongPassword(second!));
        }

        Assert.True(await _service.VerifyAsync(Email, second!));
    }

    [Fact]
    public async Task VerifyAsync_returns_true_for_correct_password_and_consumes_it()
    {
        var password = await _service.IssueAsync(Email);

        Assert.True(await _service.VerifyAsync(Email, password!));
        Assert.Empty(PasswordEntries());
        Assert.False(await _service.VerifyAsync(Email, password!));
    }

    [Fact]
    public async Task VerifyAsync_lets_only_one_of_two_simultaneous_correct_attempts_through()
    {
        var password = await _service.IssueAsync(Email);
        _store.PauseAfterReads = 2;

        var results = await Task.WhenAll(
            _service.VerifyAsync(Email, password!),
            _service.VerifyAsync(Email, password!));

        Assert.Single(results, ok => ok);
    }

    [Fact]
    public async Task VerifyAsync_accepts_password_with_surrounding_whitespace_and_differently_cased_email()
    {
        var password = await _service.IssueAsync(Email);

        Assert.True(await _service.VerifyAsync(Email.ToUpperInvariant(), $" {password} "));
    }

    [Fact]
    public async Task VerifyAsync_returns_false_when_no_password_issued()
    {
        Assert.False(await _service.VerifyAsync(Email, "123456"));
    }

    [Fact]
    public async Task VerifyAsync_returns_false_for_password_issued_in_another_tenant()
    {
        var password = await _service.IssueAsync(Email);
        _tenantContextAccessor.CurrentTenant.Returns(
            new TenantConfiguration(Guid.NewGuid(), "OtherTenant", new ConfigurationBuilder().Build(), []));

        Assert.False(await _service.VerifyAsync(Email, password!));
    }

    [Fact]
    public async Task VerifyAsync_returns_false_once_expired()
    {
        var password = await _service.IssueAsync(Email);
        _timeProvider.Advance(TestAuthPasswordService.PasswordLifetime);

        Assert.False(await _service.VerifyAsync(Email, password!));
        Assert.Empty(PasswordEntries());
    }

    [Fact]
    public async Task VerifyAsync_accepts_password_just_before_expiry()
    {
        var password = await _service.IssueAsync(Email);
        _timeProvider.Advance(TestAuthPasswordService.PasswordLifetime - TimeSpan.FromSeconds(1));

        Assert.True(await _service.VerifyAsync(Email, password!));
    }

    [Fact]
    public async Task VerifyAsync_wrong_password_counts_attempt_for_the_remaining_lifetime()
    {
        var password = await _service.IssueAsync(Email);
        _timeProvider.Advance(TimeSpan.FromMinutes(20));

        Assert.False(await _service.VerifyAsync(Email, WrongPassword(password!)));

        var attempts = Assert.Single(_store.Entries, entry => entry.Key.EndsWith(":attempts"));
        Assert.Equal(TimeSpan.FromMinutes(40), attempts.Value.Expiry);
        Assert.True(await _service.VerifyAsync(Email, password!));
    }

    [Fact]
    public async Task VerifyAsync_invalidates_password_after_max_failed_attempts()
    {
        var password = await _service.IssueAsync(Email);

        for (var i = 0; i < TestAuthPasswordService.MaxFailedAttempts; i++)
        {
            Assert.False(await _service.VerifyAsync(Email, WrongPassword(password!)));
        }

        Assert.Empty(PasswordEntries());
        Assert.False(await _service.VerifyAsync(Email, password!));
    }

    [Fact]
    public async Task VerifyAsync_caps_guesses_when_they_arrive_together()
    {
        var password = await _service.IssueAsync(Email);
        var parallelGuesses = TestAuthPasswordService.MaxFailedAttempts + 2;
        _store.PauseAfterReads = parallelGuesses;

        await Task.WhenAll(Enumerable.Range(0, parallelGuesses)
            .Select(_ => _service.VerifyAsync(Email, WrongPassword(password!))));

        Assert.Empty(PasswordEntries());
        Assert.False(await _service.VerifyAsync(Email, password!));
    }

    [Fact]
    public async Task VerifyAsync_accepts_correct_password_after_fewer_than_max_failed_attempts()
    {
        var password = await _service.IssueAsync(Email);

        for (var i = 0; i < TestAuthPasswordService.MaxFailedAttempts - 1; i++)
        {
            await _service.VerifyAsync(Email, WrongPassword(password!));
        }

        Assert.True(await _service.VerifyAsync(Email, password!));
    }

    [Fact]
    public async Task VerifyAsync_discards_unreadable_entry()
    {
        await _service.IssueAsync(Email);
        var key = Assert.Single(PasswordEntries()).Key;
        await _store.SetAsync(key, Encoding.UTF8.GetBytes("not json"), TimeSpan.FromHours(1));

        Assert.False(await _service.VerifyAsync(Email, "123456"));
        Assert.Empty(PasswordEntries());
    }

    [Fact]
    public async Task IssueAsync_issues_new_password_when_existing_entry_unreadable()
    {
        await _service.IssueAsync(Email);
        var key = Assert.Single(PasswordEntries()).Key;
        await _store.SetAsync(key, Encoding.UTF8.GetBytes("not json"), TimeSpan.FromHours(1));
        _timeProvider.Advance(TestAuthPasswordService.ResendCooldown);

        var password = await _service.IssueAsync(Email);

        Assert.NotNull(password);
        Assert.True(await _service.VerifyAsync(Email, password!));
    }

    private List<KeyValuePair<string, (byte[] Value, TimeSpan Expiry)>> PasswordEntries() =>
        _store.Entries
            .Where(entry => !entry.Key.EndsWith(":cooldown") && !entry.Key.EndsWith(":attempts"))
            .ToList();

    private static string WrongPassword(string password)
        => password == "000000" ? "111111" : "000000";

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    /// <summary>
    /// Atomic store double. Each operation is atomic, like the Redis one. <see cref="PauseAfterReads"/> holds
    /// readers until that many have read, so tests can line requests up at the same point.
    /// </summary>
    private sealed class InMemoryAtomicStore(TimeProvider timeProvider) : IAtomicCacheStore
    {
        private readonly Dictionary<string, (byte[] Value, DateTimeOffset ExpiresAt, TimeSpan Expiry)> _items = new();
        private readonly Lock _sync = new();
        private TaskCompletionSource _readersReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _readers;

        public int PauseAfterReads { get; set; }

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
            {
                _items[key] = (value, timeProvider.GetUtcNow() + expiry, expiry);
            }

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

            if (PauseAfterReads > 0)
            {
                if (Interlocked.Increment(ref _readers) >= PauseAfterReads)
                    _readersReleased.TrySetResult();
                await _readersReleased.Task.WaitAsync(TimeSpan.FromSeconds(5));
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
            {
                return Task.FromResult(_items.Remove(key));
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
}
