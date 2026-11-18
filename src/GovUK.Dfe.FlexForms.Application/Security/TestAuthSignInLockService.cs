using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.Security;

/// <inheritdoc />
public sealed class TestAuthSignInLockService(
    ITestAuthSignInLockStore lockStore,
    ITestAuthAuthenticatorService authenticatorService,
    ITenantContextAccessor tenantContextAccessor,
    TimeProvider timeProvider,
    ILogger<TestAuthSignInLockService> logger) : ITestAuthSignInLockService
{
    public async Task<bool> IsManuallyLockedAsync(string email, CancellationToken cancellationToken = default)
        => await lockStore.FindAsync(TenantId, Normalize(email), cancellationToken) is not null;

    public async Task<TestAuthSignInLockStatus> GetStatusAsync(string email, CancellationToken cancellationToken = default)
    {
        var manualLock = await lockStore.FindAsync(TenantId, Normalize(email), cancellationToken);
        var temporary = await authenticatorService.GetTemporaryLockoutAsync(email, cancellationToken);

        return new TestAuthSignInLockStatus(
            manualLock is not null,
            manualLock?.LockedAtUtc,
            manualLock?.LockedByEmail,
            temporary);
    }

    public async Task<bool> LockAsync(string email, string lockedByEmail, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = Normalize(email);
        var locked = await lockStore.LockAsync(
            TenantId,
            normalizedEmail,
            timeProvider.GetUtcNow().UtcDateTime,
            Normalize(lockedByEmail),
            cancellationToken);

        if (locked)
            logger.LogInformation("Test authentication sign-in locked for {Email} by an administrator", normalizedEmail);

        return locked;
    }

    public async Task<bool> UnlockAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = Normalize(email);
        var unlocked = await lockStore.UnlockAsync(TenantId, normalizedEmail, cancellationToken);
        await authenticatorService.ClearTemporaryLockoutAsync(normalizedEmail, cancellationToken);

        if (unlocked)
            logger.LogInformation("Test authentication sign-in unlocked for {Email} by an administrator", normalizedEmail);

        return unlocked;
    }

    public async Task ClearTemporaryLockoutAsync(string email, CancellationToken cancellationToken = default)
    {
        if (await authenticatorService.ClearTemporaryLockoutAsync(email, cancellationToken))
            logger.LogInformation("Test authentication authenticator lockout cleared for {Email} by an administrator", Normalize(email));
    }

    private Guid TenantId => tenantContextAccessor.CurrentTenant?.Id
        ?? throw new InvalidOperationException("Test authentication sign-in locks require a tenant on the current context.");

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
