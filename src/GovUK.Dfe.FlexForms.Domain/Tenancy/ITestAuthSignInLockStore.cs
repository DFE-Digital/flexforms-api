namespace GovUK.Dfe.FlexForms.Domain.Tenancy;

/// <summary>
/// Persists administrator locks on Test Authentication sign-in in the tenant configuration database.
/// </summary>
public interface ITestAuthSignInLockStore
{
    /// <summary>Returns the lock for <paramref name="normalizedEmail"/> in <paramref name="tenantId"/>, or <c>null</c>.</summary>
    Task<TestAuthSignInLock?> FindAsync(
        Guid tenantId,
        string normalizedEmail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks the email out. Idempotent: returns <c>false</c> and keeps the original lock when it was already locked.
    /// </summary>
    Task<bool> LockAsync(
        Guid tenantId,
        string normalizedEmail,
        DateTime lockedAtUtc,
        string lockedByEmail,
        CancellationToken cancellationToken = default);

    /// <summary>Removes the lock. Returns <c>true</c> when there was one.</summary>
    Task<bool> UnlockAsync(
        Guid tenantId,
        string normalizedEmail,
        CancellationToken cancellationToken = default);
}

public sealed record TestAuthSignInLock(DateTime LockedAtUtc, string LockedByEmail);
