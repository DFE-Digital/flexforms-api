using System.Text;
using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Security;

public class TestAuthAuthenticatorInviteServiceTests
{
    private const string Email = "tester@education.gov.uk";

    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));
    private readonly TimedAtomicCacheStore _cache;
    private readonly ITenantContextAccessor _tenantContextAccessor = Substitute.For<ITenantContextAccessor>();
    private readonly Guid _tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private readonly TestAuthAuthenticatorInviteService _service;

    public TestAuthAuthenticatorInviteServiceTests()
    {
        _cache = new TimedAtomicCacheStore(_clock);
        UseTenant(_tenantId);
        _service = new TestAuthAuthenticatorInviteService(
            _cache,
            _tenantContextAccessor,
            _clock,
            NullLogger<TestAuthAuthenticatorInviteService>.Instance);
    }

    [Fact]
    public async Task IssueAsync_returns_grouped_code_without_ambiguous_characters_and_expiry()
    {
        var invite = await _service.IssueAsync(Email);

        Assert.Matches("^[A-HJ-NP-Z2-9]{5}-[A-HJ-NP-Z2-9]{5}$", invite.Code);
        Assert.Equal(_clock.GetUtcNow() + TestAuthAuthenticatorInviteService.CodeLifetime, invite.ExpiresAtUtc);
    }

    [Fact]
    public async Task IssueAsync_stores_only_a_hash_under_a_tenant_scoped_key()
    {
        var invite = await _service.IssueAsync(Email);

        var (key, entry) = Assert.Single(_cache.Entries);
        Assert.StartsWith($"t:{_tenantId}:TestAuthAuthenticatorInvite_", key);
        Assert.DoesNotContain(Email, key, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TestAuthAuthenticatorInviteService.CodeLifetime, entry.Expiry);
        var stored = Encoding.UTF8.GetString(entry.Value);
        Assert.DoesNotContain(invite.Code.Replace("-", ""), stored);
    }

    [Fact]
    public async Task RedeemAsync_accepts_code_once()
    {
        var invite = await _service.IssueAsync(Email);

        Assert.True(await _service.RedeemAsync(Email, invite.Code));
        Assert.False(await _service.RedeemAsync(Email, invite.Code));
    }

    [Fact]
    public async Task RedeemAsync_ignores_case_spaces_and_hyphens_and_email_case()
    {
        var invite = await _service.IssueAsync(Email);
        var typed = " " + invite.Code.Replace("-", " ").ToLowerInvariant() + " ";

        Assert.True(await _service.RedeemAsync(Email.ToUpperInvariant(), typed));
    }

    [Fact]
    public async Task RedeemAsync_lets_only_one_parallel_request_use_the_code()
    {
        var invite = await _service.IssueAsync(Email);
        _cache.HoldReadsUntil(3);

        // All three have read the same code entry before any of them tries to consume it.
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => _service.RedeemAsync(Email, invite.Code)));

        Assert.Single(results, ok => ok);
    }

    [Fact]
    public async Task RedeemAsync_rejects_code_for_another_email()
    {
        var invite = await _service.IssueAsync(Email);

        Assert.False(await _service.RedeemAsync("someone.else@education.gov.uk", invite.Code));
        Assert.True(await _service.RedeemAsync(Email, invite.Code));
    }

    [Fact]
    public async Task RedeemAsync_rejects_code_from_another_tenant()
    {
        var invite = await _service.IssueAsync(Email);

        UseTenant(Guid.NewGuid());

        Assert.False(await _service.RedeemAsync(Email, invite.Code));
    }

    [Fact]
    public async Task RedeemAsync_rejects_expired_code()
    {
        var invite = await _service.IssueAsync(Email);
        _clock.Advance(TestAuthAuthenticatorInviteService.CodeLifetime);

        Assert.False(await _service.RedeemAsync(Email, invite.Code));
    }

    [Fact]
    public async Task RedeemAsync_discards_code_after_max_wrong_attempts()
    {
        var invite = await _service.IssueAsync(Email);
        for (var i = 0; i < TestAuthAuthenticatorInviteService.MaxFailedAttempts; i++)
            Assert.False(await _service.RedeemAsync(Email, "WRONG-CODE2"));

        Assert.False(await _service.RedeemAsync(Email, invite.Code));
        Assert.Empty(_cache.Entries);
    }

    [Fact]
    public async Task IssueAsync_replaces_unused_code_for_the_same_email()
    {
        var first = await _service.IssueAsync(Email);
        var second = await _service.IssueAsync(Email);

        Assert.False(await _service.RedeemAsync(Email, first.Code));
        Assert.True(await _service.RedeemAsync(Email, second.Code));
    }

    [Fact]
    public async Task RedeemAsync_returns_false_when_no_code_was_issued()
    {
        Assert.False(await _service.RedeemAsync(Email, "ABCDE-FGHJK"));
    }

    private void UseTenant(Guid tenantId)
        => _tenantContextAccessor.CurrentTenant.Returns(
            new TenantConfiguration(tenantId, "TestTenant", new ConfigurationBuilder().Build(), []));
}
