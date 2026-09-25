namespace GovUK.Dfe.FlexForms.Application.Security;

/// <summary>
/// Issues and verifies the one-time passwords that gate Test Authentication sign-in.
/// Passwords are tenant-scoped, stored in Redis and expire after <see cref="TestAuthPasswordService.PasswordLifetime"/>.
/// </summary>
public interface ITestAuthPasswordService
{
    /// <summary>
    /// Generates a new random 6 digit password for <paramref name="email"/>, replacing any existing one.
    /// </summary>
    /// <returns>
    /// The plaintext password to email to the user, or <c>null</c> when a password was issued for this
    /// email within <see cref="TestAuthPasswordService.ResendCooldown"/> (the earlier password remains valid).
    /// </returns>
    Task<string?> IssueAsync(string email);

    /// <summary>
    /// Checks <paramref name="password"/> against the stored password for <paramref name="email"/>.
    /// A match consumes the password. Too many failed attempts invalidate it.
    /// </summary>
    Task<bool> VerifyAsync(string email, string password);
}
