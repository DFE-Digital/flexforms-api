using System.Text;
using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Domain.Caching;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OtpNet;

namespace GovUK.Dfe.FlexForms.Application.Tests.Security;

public class TestAuthAuthenticatorServiceTests
{
    private const string Email = "tester@education.gov.uk";
    private const string Issuer = "Transfers (Test)";

    private readonly ManualClock _timeProvider = new(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));
    private readonly TimedAtomicCacheStore _cache;
    private readonly InMemoryAuthenticatorStore _authenticators = new();
    private readonly ITenantContextAccessor _tenantContextAccessor = Substitute.For<ITenantContextAccessor>();
    private readonly Guid _tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private readonly TestAuthAuthenticatorService _service;

    public TestAuthAuthenticatorServiceTests()
    {
        _cache = new TimedAtomicCacheStore(_timeProvider);
        UseTenant(_tenantId);

        _service = new TestAuthAuthenticatorService(
            _cache,
            _authenticators,
            new PrefixProtector(),
            _tenantContextAccessor,
            _timeProvider,
            NullLogger<TestAuthAuthenticatorService>.Instance);
    }

    [Fact]
    public async Task BeginSetupAsync_returns_base32_key_and_otpauth_uri_without_saving()
    {
        var setup = await _service.BeginSetupAsync(Email, Issuer);

        Assert.Equal(20, Base32Encoding.ToBytes(setup.SharedKey).Length);
        Assert.Equal(
            $"otpauth://totp/Transfers%20%28Test%29:tester%40education.gov.uk?secret={setup.SharedKey}" +
            "&issuer=Transfers%20%28Test%29&algorithm=SHA1&digits=6&period=30",
            setup.AuthenticatorUri);
        Assert.Empty(_authenticators.Entries);
    }

    [Fact]
    public async Task BeginSetupAsync_holds_pending_secret_encrypted_and_tenant_scoped()
    {
        var setup = await _service.BeginSetupAsync(Email, Issuer);

        var (key, entry) = Assert.Single(_cache.Entries);
        Assert.StartsWith($"t:{_tenantId}:TestAuthAuthenticator_", key);
        Assert.DoesNotContain(Email, key, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TestAuthAuthenticatorService.PendingSetupLifetime, entry.Expiry);
        var stored = Encoding.UTF8.GetString(entry.Value);
        Assert.Contains(PrefixProtector.Prefix, stored);
        Assert.DoesNotContain(setup.SharedKey, stored);
    }

    [Fact]
    public async Task ConfirmSetupAsync_saves_enrolment_when_code_matches()
    {
        var setup = await _service.BeginSetupAsync(Email, Issuer);

        Assert.True(await _service.ConfirmSetupAsync(Email, CodeFor(setup.SharedKey)));

        var saved = Assert.Single(_authenticators.Entries);
        Assert.Equal((_tenantId, Email), saved.Key);
        Assert.Equal(Base32Encoding.ToBytes(setup.SharedKey), saved.Value.Secret);
        Assert.Equal(_timeProvider.GetUtcNow().UtcDateTime, saved.Value.CreatedAtUtc);
        Assert.Equal(_timeProvider.GetUtcNow().UtcDateTime, await _service.GetEnrolledOnAsync(Email));
    }

    [Fact]
    public async Task ConfirmSetupAsync_consumes_pending_setup()
    {
        var setup = await _service.BeginSetupAsync(Email, Issuer);
        await _service.ConfirmSetupAsync(Email, CodeFor(setup.SharedKey));
        _timeProvider.Advance(TimeSpan.FromSeconds(30));

        Assert.False(await _service.ConfirmSetupAsync(Email, CodeFor(setup.SharedKey)));
    }

    [Fact]
    public async Task ConfirmSetupAsync_returns_false_without_pending_setup()
    {
        Assert.False(await _service.ConfirmSetupAsync(Email, "123456"));
        Assert.Empty(_authenticators.Entries);
    }

    [Fact]
    public async Task ConfirmSetupAsync_returns_false_for_wrong_code_and_keeps_pending_setup()
    {
        var setup = await _service.BeginSetupAsync(Email, Issuer);

        Assert.False(await _service.ConfirmSetupAsync(Email, WrongCode(setup.SharedKey)));
        Assert.Empty(_authenticators.Entries);
        Assert.True(await _service.ConfirmSetupAsync(Email, CodeFor(setup.SharedKey)));
    }

    [Fact]
    public async Task ConfirmSetupAsync_discards_pending_setup_after_max_wrong_codes()
    {
        var setup = await _service.BeginSetupAsync(Email, Issuer);
        for (var i = 0; i < TestAuthAuthenticatorService.MaxFailedAttempts; i++)
            Assert.False(await _service.ConfirmSetupAsync(Email, WrongCode(setup.SharedKey)));

        Assert.False(await _service.ConfirmSetupAsync(Email, CodeFor(setup.SharedKey)));
        Assert.Empty(_authenticators.Entries);
    }

    [Fact]
    public async Task ConfirmSetupAsync_after_expiry_returns_false()
    {
        var setup = await _service.BeginSetupAsync(Email, Issuer);
        _timeProvider.Advance(TestAuthAuthenticatorService.PendingSetupLifetime);

        Assert.False(await _service.ConfirmSetupAsync(Email, CodeFor(setup.SharedKey)));
    }

    [Fact]
    public async Task Starting_setup_again_keeps_existing_enrolment_until_confirmed()
    {
        var first = await EnrolAsync();
        _timeProvider.Advance(TimeSpan.FromMinutes(1));

        var second = await _service.BeginSetupAsync(Email, Issuer);

        Assert.True(await _service.VerifyAsync(Email, CodeFor(first)));
        _timeProvider.Advance(TimeSpan.FromSeconds(30));
        Assert.True(await _service.ConfirmSetupAsync(Email, CodeFor(second.SharedKey)));
        _timeProvider.Advance(TimeSpan.FromSeconds(30));
        Assert.False(await _service.VerifyAsync(Email, CodeFor(first)));
        Assert.True(await _service.VerifyAsync(Email, CodeFor(second.SharedKey)));
    }

    [Fact]
    public async Task VerifyAsync_accepts_current_code_and_records_use()
    {
        var sharedKey = await EnrolAsync();
        _timeProvider.Advance(TimeSpan.FromMinutes(1));

        Assert.True(await _service.VerifyAsync(Email, CodeFor(sharedKey)));
        Assert.Equal(_timeProvider.GetUtcNow().UtcDateTime, _authenticators.LastUsed[(_tenantId, Email)]);
    }

    [Fact]
    public async Task VerifyAsync_accepts_each_code_only_once()
    {
        var sharedKey = await EnrolAsync();
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        var code = CodeFor(sharedKey);

        Assert.True(await _service.VerifyAsync(Email, code));
        Assert.False(await _service.VerifyAsync(Email, code));
    }

    [Fact]
    public async Task VerifyAsync_lets_only_one_parallel_request_use_a_code()
    {
        var sharedKey = await EnrolAsync();
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        var code = CodeFor(sharedKey);
        _authenticators.HoldFindsUntil(3);

        // All three have looked up the enrolment and matched the code before any of them marks it used.
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => _service.VerifyAsync(Email, code)));

        Assert.Single(results, ok => ok);
    }

    [Fact]
    public async Task ConfirmSetupAsync_delayed_does_not_overwrite_a_newer_enrolment()
    {
        var older = await _service.BeginSetupAsync(Email, Issuer);
        string? newerKey = null;
        _authenticators.BeforeNextSave = async () =>
        {
            // The older confirmation has consumed its setup but not saved yet; a newer setup completes meanwhile.
            var newer = await _service.BeginSetupAsync(Email, Issuer);
            newerKey = newer.SharedKey;
            Assert.True(await _service.ConfirmSetupAsync(Email, CodeFor(newer.SharedKey)));
        };

        Assert.False(await _service.ConfirmSetupAsync(Email, CodeFor(older.SharedKey)));

        Assert.Equal(Base32Encoding.ToBytes(newerKey!), Assert.Single(_authenticators.Entries).Value.Secret);
    }

    [Fact]
    public async Task ConfirmSetupAsync_delayed_does_not_recreate_a_removed_enrolment()
    {
        await EnrolAsync();
        AdvanceToNextStep();
        var replacement = await _service.BeginSetupAsync(Email, Issuer);
        _authenticators.BeforeNextSave = async () => Assert.True(await _service.RemoveAsync(Email));

        Assert.False(await _service.ConfirmSetupAsync(Email, CodeFor(replacement.SharedKey)));

        Assert.Empty(_authenticators.Entries);
    }

    [Fact]
    public async Task ConfirmSetupAsync_replaces_the_enrolment_that_was_current_when_setup_began()
    {
        var first = await EnrolAsync();
        AdvanceToNextStep();
        var second = await _service.BeginSetupAsync(Email, Issuer);

        Assert.True(await _service.ConfirmSetupAsync(Email, CodeFor(second.SharedKey)));

        var saved = Assert.Single(_authenticators.Entries).Value.Secret;
        Assert.Equal(Base32Encoding.ToBytes(second.SharedKey), saved);
        Assert.NotEqual(Base32Encoding.ToBytes(first), saved);
    }

    private void AdvanceToNextStep() => _timeProvider.Advance(TimeSpan.FromSeconds(30));

    [Fact]
    public async Task VerifyAsync_allows_one_step_of_clock_drift_but_not_two()
    {
        var sharedKey = await EnrolAsync();
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        var previousStepCode = CodeFor(sharedKey, _timeProvider.GetUtcNow().AddSeconds(-30));
        var twoStepsOldCode = CodeFor(sharedKey, _timeProvider.GetUtcNow().AddSeconds(-60));

        Assert.False(await _service.VerifyAsync(Email, twoStepsOldCode));
        Assert.True(await _service.VerifyAsync(Email, previousStepCode));
    }

    [Fact]
    public async Task VerifyAsync_normalises_email_and_trims_code()
    {
        var sharedKey = await EnrolAsync();
        _timeProvider.Advance(TimeSpan.FromMinutes(1));

        Assert.True(await _service.VerifyAsync($"  {Email.ToUpperInvariant()} ", $" {CodeFor(sharedKey)} "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    public async Task VerifyAsync_rejects_malformed_codes(string code)
    {
        await EnrolAsync();

        Assert.False(await _service.VerifyAsync(Email, code));
    }

    [Fact]
    public async Task VerifyAsync_returns_false_when_not_enrolled()
    {
        Assert.False(await _service.VerifyAsync(Email, "123456"));
    }

    [Fact]
    public async Task VerifyAsync_locks_out_after_max_failed_attempts_even_with_correct_code()
    {
        var sharedKey = await EnrolAsync();
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        for (var i = 0; i < TestAuthAuthenticatorService.MaxFailedAttempts; i++)
            Assert.False(await _service.VerifyAsync(Email, WrongCode(sharedKey)));

        Assert.False(await _service.VerifyAsync(Email, CodeFor(sharedKey)));
    }

    [Fact]
    public async Task VerifyAsync_lockout_ends_after_lockout_window()
    {
        var sharedKey = await EnrolAsync();
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        for (var i = 0; i <= TestAuthAuthenticatorService.MaxFailedAttempts; i++)
            await _service.VerifyAsync(Email, WrongCode(sharedKey));

        _timeProvider.Advance(TestAuthAuthenticatorService.LockoutWindow);

        Assert.True(await _service.VerifyAsync(Email, CodeFor(sharedKey)));
    }

    [Fact]
    public async Task VerifyAsync_counts_attempts_for_emails_without_enrolment()
    {
        for (var i = 0; i < TestAuthAuthenticatorService.MaxFailedAttempts; i++)
            await _service.VerifyAsync(Email, "123456");

        var sharedKey = await EnrolAsync();
        _timeProvider.Advance(TimeSpan.FromSeconds(30));

        Assert.False(await _service.VerifyAsync(Email, CodeFor(sharedKey)));
    }

    [Fact]
    public async Task VerifyAsync_success_resets_failed_attempts()
    {
        var sharedKey = await EnrolAsync();
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        for (var i = 0; i < TestAuthAuthenticatorService.MaxFailedAttempts - 1; i++)
            await _service.VerifyAsync(Email, WrongCode(sharedKey));
        Assert.True(await _service.VerifyAsync(Email, CodeFor(sharedKey)));

        _timeProvider.Advance(TimeSpan.FromSeconds(30));
        for (var i = 0; i < TestAuthAuthenticatorService.MaxFailedAttempts - 1; i++)
            await _service.VerifyAsync(Email, WrongCode(sharedKey));

        Assert.True(await _service.VerifyAsync(Email, CodeFor(sharedKey)));
    }

    [Fact]
    public async Task VerifyAsync_does_not_accept_enrolment_from_another_tenant()
    {
        var sharedKey = await EnrolAsync();
        _timeProvider.Advance(TimeSpan.FromMinutes(1));

        UseTenant(Guid.NewGuid());

        Assert.False(await _service.VerifyAsync(Email, CodeFor(sharedKey)));
    }

    [Fact]
    public async Task RemoveAsync_deletes_enrolment()
    {
        var sharedKey = await EnrolAsync();

        Assert.True(await _service.RemoveAsync(Email));
        Assert.False(await _service.RemoveAsync(Email));
        Assert.Null(await _service.GetEnrolledOnAsync(Email));
        _timeProvider.Advance(TimeSpan.FromSeconds(30));
        Assert.False(await _service.VerifyAsync(Email, CodeFor(sharedKey)));
    }

    [Fact]
    public async Task RemoveAsync_discards_pending_setup()
    {
        var setup = await _service.BeginSetupAsync(Email, Issuer);

        await _service.RemoveAsync(Email);

        Assert.False(await _service.ConfirmSetupAsync(Email, CodeFor(setup.SharedKey)));
    }

    [Fact]
    public void BuildAuthenticatorUri_removes_colons_from_issuer()
    {
        var uri = TestAuthAuthenticatorService.BuildAuthenticatorUri("Service: Test", Email, "ABC");

        Assert.StartsWith("otpauth://totp/Service%20%20Test:tester%40education.gov.uk?", uri);
        Assert.Contains("&issuer=Service%20%20Test&", uri);
    }

    [Fact]
    public async Task ConfirmInviteSetupAsync_completes_setup_started_from_a_setup_code()
    {
        var setup = await _service.BeginInviteSetupAsync(Email, Issuer);

        Assert.True(await _service.ConfirmInviteSetupAsync(Email, CodeFor(setup.SharedKey)));

        Assert.Equal(Base32Encoding.ToBytes(setup.SharedKey), Assert.Single(_authenticators.Entries).Value.Secret);
    }

    [Fact]
    public async Task ConfirmInviteSetupAsync_refuses_a_signed_in_users_own_setup_without_using_its_attempts()
    {
        var setup = await _service.BeginSetupAsync(Email, Issuer);

        for (var i = 0; i < TestAuthAuthenticatorService.MaxFailedAttempts; i++)
            Assert.False(await _service.ConfirmInviteSetupAsync(Email, WrongCode(setup.SharedKey)));
        Assert.False(await _service.ConfirmInviteSetupAsync(Email, CodeFor(setup.SharedKey)));

        Assert.Empty(_authenticators.Entries);
        Assert.True(await _service.ConfirmSetupAsync(Email, CodeFor(setup.SharedKey)));
    }

    [Fact]
    public async Task ConfirmSetupAsync_also_completes_setup_started_from_a_setup_code()
    {
        var setup = await _service.BeginInviteSetupAsync(Email, Issuer);

        Assert.True(await _service.ConfirmSetupAsync(Email, CodeFor(setup.SharedKey)));
    }

    private async Task<string> EnrolAsync()
    {
        var setup = await _service.BeginSetupAsync(Email, Issuer);
        Assert.True(await _service.ConfirmSetupAsync(Email, CodeFor(setup.SharedKey)));
        return setup.SharedKey;
    }

    private string CodeFor(string sharedKey) => CodeFor(sharedKey, _timeProvider.GetUtcNow());

    private static string CodeFor(string sharedKey, DateTimeOffset at)
        => new Totp(Base32Encoding.ToBytes(sharedKey)).ComputeTotp(at.UtcDateTime);

    // Not valid at any step the service accepts (one either side of now), so wrong-code tests cannot pass by chance.
    private string WrongCode(string sharedKey)
    {
        var totp = new Totp(Base32Encoding.ToBytes(sharedKey));
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        return Enumerable.Range(0, 4)
            .Select(value => value.ToString("D6", System.Globalization.CultureInfo.InvariantCulture))
            .First(code => !totp.VerifyTotp(now, code, out _, new VerificationWindow(previous: 1, future: 1)));
    }

    private void UseTenant(Guid tenantId)
        => _tenantContextAccessor.CurrentTenant.Returns(
            new TenantConfiguration(tenantId, "TestTenant", new ConfigurationBuilder().Build(), []));

    private sealed class PrefixProtector : ITestAuthAuthenticatorSecretProtector
    {
        public const string Prefix = "protected:";

        public string Protect(byte[] secret) => Prefix + Convert.ToBase64String(secret);

        public byte[]? Unprotect(string protectedSecret)
            => protectedSecret.StartsWith(Prefix, StringComparison.Ordinal)
                ? Convert.FromBase64String(protectedSecret[Prefix.Length..])
                : null;
    }

    /// <summary>
    /// Enrolment store double with the same compare-and-swap rule as the SQL store. <see cref="BeforeNextSave"/> runs
    /// once, inside the next save and before its check, so a test can complete another request at that exact point.
    /// <see cref="HoldFindsUntil"/> keeps lookups waiting until that many callers have looked up, so requests overlap.
    /// </summary>
    private sealed class InMemoryAuthenticatorStore : ITestAuthAuthenticatorStore
    {
        private readonly Lock _sync = new();
        private readonly Dictionary<(Guid, string), (TestAuthAuthenticatorEnrolment Enrolment, string SetupId)> _entries = new();
        private TaskCompletionSource? _findsReleased;
        private int _findsToHold;
        private int _finds;

        public Dictionary<(Guid, string), DateTime> LastUsed { get; } = new();

        public Func<Task>? BeforeNextSave { get; set; }

        public IReadOnlyDictionary<(Guid, string), TestAuthAuthenticatorEnrolment> Entries
        {
            get
            {
                lock (_sync)
                    return _entries.ToDictionary(entry => entry.Key, entry => entry.Value.Enrolment);
            }
        }

        public void HoldFindsUntil(int callers)
        {
            _findsToHold = callers;
            _finds = 0;
            _findsReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public async Task<TestAuthAuthenticatorEnrolment?> FindAsync(Guid tenantId, string normalizedEmail, CancellationToken cancellationToken = default)
        {
            TestAuthAuthenticatorEnrolment? enrolment;
            lock (_sync)
                enrolment = _entries.TryGetValue((tenantId, normalizedEmail), out var entry) ? entry.Enrolment : null;

            if (_findsReleased is { } released)
            {
                if (Interlocked.Increment(ref _finds) >= _findsToHold)
                    released.TrySetResult();
                await released.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }

            return enrolment;
        }

        public Task<string?> GetCurrentSetupIdAsync(Guid tenantId, string normalizedEmail, CancellationToken cancellationToken = default)
        {
            lock (_sync)
                return Task.FromResult(_entries.TryGetValue((tenantId, normalizedEmail), out var entry) ? entry.SetupId : null);
        }

        public async Task<bool> SaveAsync(
            Guid tenantId,
            string normalizedEmail,
            byte[] secret,
            DateTime createdAtUtc,
            string setupId,
            string? expectedCurrentSetupId,
            CancellationToken cancellationToken = default)
        {
            if (BeforeNextSave is { } beforeSave)
            {
                BeforeNextSave = null;
                await beforeSave();
            }

            lock (_sync)
            {
                var key = (tenantId, normalizedEmail);
                var currentSetupId = _entries.TryGetValue(key, out var current) ? current.SetupId : null;
                if (currentSetupId != expectedCurrentSetupId)
                    return false;

                _entries[key] = (new TestAuthAuthenticatorEnrolment(secret, createdAtUtc), setupId);
                return true;
            }
        }

        public Task MarkUsedAsync(Guid tenantId, string normalizedEmail, DateTime usedAtUtc, CancellationToken cancellationToken = default)
        {
            lock (_sync)
                LastUsed[(tenantId, normalizedEmail)] = usedAtUtc;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(Guid tenantId, string normalizedEmail, CancellationToken cancellationToken = default)
        {
            lock (_sync)
                return Task.FromResult(_entries.Remove((tenantId, normalizedEmail)));
        }
    }
}
