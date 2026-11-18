namespace GovUK.Dfe.FlexForms.Application.Security;

/// <summary>
/// Authenticator app (TOTP) sign-in for Test Authentication, as an alternative to the emailed one-time password.
/// Enrolments are per tenant and per email.
/// </summary>
public interface ITestAuthAuthenticatorService
{
    /// <summary>Returns when the authenticator for <paramref name="email"/> was enrolled, or <c>null</c> if none.</summary>
    Task<DateTime?> GetEnrolledOnAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a new secret for <paramref name="email"/> and holds it as a pending enrolment for
    /// <see cref="TestAuthAuthenticatorService.PendingSetupLifetime"/>. An existing enrolment keeps working until a
    /// code for the new secret is confirmed.
    /// </summary>
    Task<TestAuthAuthenticatorSetup> BeginSetupAsync(string email, string issuer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the pending enrolment when <paramref name="code"/> matches it. Too many wrong codes discard it. Returns
    /// <c>false</c> without saving if the enrolment was replaced or removed after this setup began.
    /// </summary>
    Task<bool> ConfirmSetupAsync(string email, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Same as <see cref="BeginSetupAsync"/>, for a user who redeemed an administrator-issued setup code instead of
    /// signing in. Only <see cref="ConfirmInviteSetupAsync"/> can complete it anonymously.
    /// </summary>
    Task<TestAuthAuthenticatorSetup> BeginInviteSetupAsync(string email, string issuer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Anonymous counterpart of <see cref="ConfirmSetupAsync"/>: only completes a pending enrolment started by
    /// <see cref="BeginInviteSetupAsync"/>, so callers who are not signed in cannot touch a signed-in user's setup.
    /// </summary>
    Task<bool> ConfirmInviteSetupAsync(string email, string code, CancellationToken cancellationToken = default);

    /// <summary>Removes the enrolment. Returns <c>true</c> when one existed.</summary>
    Task<bool> RemoveAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks a sign-in code. Each code is accepted once. After <see cref="TestAuthAuthenticatorService.MaxFailedAttempts"/>
    /// attempts without success the email is locked out until <see cref="TestAuthAuthenticatorService.LockoutWindow"/>
    /// after the first of them; the result then reports <see cref="TestAuthLockout.Temporary"/> and the time left.
    /// Does not check administrator locks: callers check <see cref="ITestAuthSignInLockService"/> first.
    /// </summary>
    Task<TestAuthSignInResult> VerifyAsync(string email, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns how long until authenticator sign-in for <paramref name="email"/> unlocks, or <c>null</c> when it is not
    /// temporarily locked out.
    /// </summary>
    Task<TimeSpan?> GetTemporaryLockoutAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Clears the failed-attempt count, ending any temporary lockout. Returns <c>true</c> when there was a count.</summary>
    Task<bool> ClearTemporaryLockoutAsync(string email, CancellationToken cancellationToken = default);
}

public sealed record TestAuthAuthenticatorSetup(string SharedKey, string AuthenticatorUri);
