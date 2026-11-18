using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Security;

public class TestAuthSignInLockServiceTests
{
    private const string Email = "tester@education.gov.uk";
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ITestAuthSignInLockStore _store = Substitute.For<ITestAuthSignInLockStore>();
    private readonly ITestAuthAuthenticatorService _authenticator = Substitute.For<ITestAuthAuthenticatorService>();
    private readonly TestAuthSignInLockService _service;

    public TestAuthSignInLockServiceTests()
    {
        var tenantContext = Substitute.For<ITenantContextAccessor>();
        tenantContext.CurrentTenant.Returns(new TenantConfiguration(
            _tenantId,
            "TestTenant",
            new ConfigurationBuilder().Build(),
            []));

        _service = new TestAuthSignInLockService(
            _store,
            _authenticator,
            tenantContext,
            new ManualClock(Now),
            NullLogger<TestAuthSignInLockService>.Instance);
    }

    [Fact]
    public async Task IsManuallyLocked_normalises_the_email()
    {
        _store.FindAsync(_tenantId, Email, Arg.Any<CancellationToken>()).Returns(new TestAuthSignInLock(Now.UtcDateTime, "admin@x"));

        Assert.True(await _service.IsManuallyLockedAsync($" {Email.ToUpperInvariant()} "));
    }

    [Fact]
    public async Task Lock_records_when_and_by_whom()
    {
        _store.LockAsync(_tenantId, Email, Now.UtcDateTime, "admin@x", Arg.Any<CancellationToken>()).Returns(true);

        Assert.True(await _service.LockAsync(Email, " Admin@X "));
    }

    [Fact]
    public async Task Unlock_also_clears_the_temporary_lockout()
    {
        _store.UnlockAsync(_tenantId, Email, Arg.Any<CancellationToken>()).Returns(true);

        Assert.True(await _service.UnlockAsync(Email));

        await _authenticator.Received(1).ClearTemporaryLockoutAsync(Email, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Status_combines_manual_and_temporary_locks()
    {
        _store.FindAsync(_tenantId, Email, Arg.Any<CancellationToken>()).Returns(new TestAuthSignInLock(Now.UtcDateTime, "admin@x"));
        _authenticator.GetTemporaryLockoutAsync(Email, Arg.Any<CancellationToken>()).Returns(TimeSpan.FromMinutes(4));

        var status = await _service.GetStatusAsync(Email);

        Assert.True(status.IsManuallyLocked);
        Assert.Equal(Now.UtcDateTime, status.LockedAtUtc);
        Assert.Equal("admin@x", status.LockedByEmail);
        Assert.Equal(TimeSpan.FromMinutes(4), status.TemporaryLockoutRemaining);
    }
}
