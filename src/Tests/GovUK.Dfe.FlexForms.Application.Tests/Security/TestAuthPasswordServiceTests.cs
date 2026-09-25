using System.Text;
using GovUK.Dfe.CoreLibs.Caching.Interfaces;
using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Security;

public class TestAuthPasswordServiceTests
{
    private const string Email = "tester@education.gov.uk";

    private readonly InMemoryRedisCacheService _redis = new();
    private readonly ITenantContextAccessor _tenantContextAccessor = Substitute.For<ITenantContextAccessor>();
    private readonly ManualTimeProvider _timeProvider = new(new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero));
    private readonly Guid _tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private readonly TestAuthPasswordService _service;

    public TestAuthPasswordServiceTests()
    {
        _tenantContextAccessor.CurrentTenant.Returns(
            new TenantConfiguration(_tenantId, "TestTenant", new ConfigurationBuilder().Build(), []));

        _service = new TestAuthPasswordService(
            _redis,
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

        var (key, entry) = Assert.Single(_redis.Entries);
        Assert.Equal(TimeSpan.FromHours(1), entry.Expiry);
        Assert.Equal(TestAuthPasswordService.PasswordLifetime, entry.Expiry);
        Assert.StartsWith($"t:{_tenantId}:TestAuthPassword_", key);
    }

    [Fact]
    public async Task IssueAsync_does_not_store_plaintext_password_or_email()
    {
        var password = await _service.IssueAsync(Email);

        var (key, entry) = Assert.Single(_redis.Entries);
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

        Assert.Single(_redis.Entries);
    }

    [Fact]
    public async Task IssueAsync_scopes_key_to_tenant()
    {
        await _service.IssueAsync(Email);

        var otherTenantId = Guid.NewGuid();
        _tenantContextAccessor.CurrentTenant.Returns(
            new TenantConfiguration(otherTenantId, "OtherTenant", new ConfigurationBuilder().Build(), []));
        await _service.IssueAsync(Email);

        Assert.Equal(2, _redis.Entries.Count);
        Assert.Contains(_redis.Entries.Keys, k => k.StartsWith($"t:{otherTenantId}:"));
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
    public async Task VerifyAsync_returns_true_for_correct_password_and_consumes_it()
    {
        var password = await _service.IssueAsync(Email);

        Assert.True(await _service.VerifyAsync(Email, password!));
        Assert.Empty(_redis.Entries);
        Assert.False(await _service.VerifyAsync(Email, password!));
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
    public async Task VerifyAsync_returns_false_and_removes_entry_once_expired()
    {
        var password = await _service.IssueAsync(Email);
        _timeProvider.Advance(TestAuthPasswordService.PasswordLifetime);

        Assert.False(await _service.VerifyAsync(Email, password!));
        Assert.Empty(_redis.Entries);
    }

    [Fact]
    public async Task VerifyAsync_accepts_password_just_before_expiry()
    {
        var password = await _service.IssueAsync(Email);
        _timeProvider.Advance(TestAuthPasswordService.PasswordLifetime - TimeSpan.FromSeconds(1));

        Assert.True(await _service.VerifyAsync(Email, password!));
    }

    [Fact]
    public async Task VerifyAsync_wrong_password_records_attempt_and_keeps_remaining_lifetime()
    {
        var password = await _service.IssueAsync(Email);
        _timeProvider.Advance(TimeSpan.FromMinutes(20));

        Assert.False(await _service.VerifyAsync(Email, WrongPassword(password!)));

        var (_, entry) = Assert.Single(_redis.Entries);
        Assert.Equal(TimeSpan.FromMinutes(40), entry.Expiry);
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

        Assert.Empty(_redis.Entries);
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
        var key = Assert.Single(_redis.Entries).Key;
        await _redis.SetRawAsync(key, Encoding.UTF8.GetBytes("not json"), TimeSpan.FromHours(1));

        Assert.False(await _service.VerifyAsync(Email, "123456"));
        Assert.Empty(_redis.Entries);
    }

    [Fact]
    public async Task IssueAsync_issues_new_password_when_existing_entry_unreadable()
    {
        await _service.IssueAsync(Email);
        var key = Assert.Single(_redis.Entries).Key;
        await _redis.SetRawAsync(key, Encoding.UTF8.GetBytes("not json"), TimeSpan.FromHours(1));

        var password = await _service.IssueAsync(Email);

        Assert.NotNull(password);
        Assert.True(await _service.VerifyAsync(Email, password!));
    }

    private static string WrongPassword(string password)
        => password == "000000" ? "111111" : "000000";

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class InMemoryRedisCacheService : IAdvancedRedisCacheService
    {
        public Dictionary<string, (byte[] Value, TimeSpan Expiry)> Entries { get; } = new();

        public Type CacheType => typeof(IRedisCacheType);

        public Task SetRawAsync(string cacheKey, byte[] value, TimeSpan expiry)
        {
            Entries[cacheKey] = (value, expiry);
            return Task.CompletedTask;
        }

        public Task<byte[]?> GetRawAsync(string cacheKey)
            => Task.FromResult(Entries.TryGetValue(cacheKey, out var entry) ? entry.Value : null);

        public Task RemoveAsync(string cacheKey)
        {
            Entries.Remove(cacheKey);
            return Task.CompletedTask;
        }

        public Task RemoveByPatternAsync(string pattern) => throw new NotSupportedException();

        public Task<T> GetOrAddAsync<T>(string cacheKey, Func<Task<T>> fetchFunction, string methodName,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<T?> GetAsync<T>(string cacheKey) => throw new NotSupportedException();

        public void Remove(string cacheKey) => Entries.Remove(cacheKey);
    }
}
