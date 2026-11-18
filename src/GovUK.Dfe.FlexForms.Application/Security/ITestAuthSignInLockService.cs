namespace GovUK.Dfe.FlexForms.Application.Security;

/// <summary>
/// Administrator locks on Test Authentication sign-in, plus the temporary authenticator lockout administrators can see
/// and clear. A manual lock refuses every Test Authentication sign-in method for the email in the current tenant and
/// never expires.
/// </summary>
public interface ITestAuthSignInLockService
{
    /// <summary><c>true</c> when an administrator has locked <paramref name="email"/> out.</summary>
    Task<bool> IsManuallyLockedAsync(string email, CancellationToken cancellationToken = default);

    Task<TestAuthSignInLockStatus> GetStatusAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Locks the email out until an administrator unlocks it. Returns <c>false</c> when it was already locked.</summary>
    Task<bool> LockAsync(string email, string lockedByEmail, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the manual lock and any temporary lockout, so the user can sign in straight away. Returns <c>true</c>
    /// when there was a manual lock.
    /// </summary>
    Task<bool> UnlockAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Ends a temporary lockout from wrong codes. Leaves a manual lock in place.</summary>
    Task ClearTemporaryLockoutAsync(string email, CancellationToken cancellationToken = default);
}

/// <param name="IsManuallyLocked">An administrator has locked the email out.</param>
/// <param name="LockedAtUtc">When the manual lock was set.</param>
/// <param name="LockedByEmail">Who set the manual lock.</param>
/// <param name="TemporaryLockoutRemaining">Time left on a temporary lockout, or <c>null</c> when there is none.</param>
public sealed record TestAuthSignInLockStatus(
    bool IsManuallyLocked,
    DateTime? LockedAtUtc,
    string? LockedByEmail,
    TimeSpan? TemporaryLockoutRemaining);
