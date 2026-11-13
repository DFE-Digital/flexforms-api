namespace GovUK.Dfe.FlexForms.Domain.Tenancy;

/// <summary>
/// Persists Test Authentication authenticator app enrolments in the tenant configuration database.
/// Secrets are encrypted at rest; callers only ever see the plaintext secret bytes.
/// </summary>
public interface ITestAuthAuthenticatorStore
{
    /// <summary>
    /// Returns the enrolment for <paramref name="normalizedEmail"/> in <paramref name="tenantId"/>, or <c>null</c>
    /// when there is none or its secret can no longer be decrypted.
    /// </summary>
    Task<TestAuthAuthenticatorEnrolment?> FindAsync(
        Guid tenantId,
        string normalizedEmail,
        CancellationToken cancellationToken = default);

    /// <summary>Saves the enrolment, replacing any existing one for the same tenant and email.</summary>
    Task SaveAsync(
        Guid tenantId,
        string normalizedEmail,
        byte[] secret,
        DateTime createdAtUtc,
        CancellationToken cancellationToken = default);

    Task MarkUsedAsync(
        Guid tenantId,
        string normalizedEmail,
        DateTime usedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Removes the enrolment. Returns <c>true</c> when one existed.</summary>
    Task<bool> DeleteAsync(
        Guid tenantId,
        string normalizedEmail,
        CancellationToken cancellationToken = default);
}

public sealed record TestAuthAuthenticatorEnrolment(byte[] Secret, DateTime CreatedAtUtc);
