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

    /// <summary>
    /// Returns the current generation for this tenant/email: the setup id of the enrolment (even when its secret
    /// cannot be decrypted), or of the removal if it was removed. <c>null</c> only when there has never been an
    /// enrolment. Pass it to <see cref="SaveAsync"/> as the generation a new setup replaces.
    /// </summary>
    Task<string?> GetCurrentSetupIdAsync(
        Guid tenantId,
        string normalizedEmail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the enrolment produced by setup <paramref name="setupId"/>, but only while the current generation is
    /// still <paramref name="expectedCurrentSetupId"/> (<c>null</c> meaning there has never been an enrolment).
    /// Returns <c>false</c> without saving when it has since been replaced or removed.
    /// </summary>
    Task<bool> SaveAsync(
        Guid tenantId,
        string normalizedEmail,
        byte[] secret,
        DateTime createdAtUtc,
        string setupId,
        string? expectedCurrentSetupId,
        CancellationToken cancellationToken = default);

    /// <summary>Best effort: does nothing if the enrolment was replaced or removed meanwhile.</summary>
    Task MarkUsedAsync(
        Guid tenantId,
        string normalizedEmail,
        DateTime usedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the enrolment, keeping a tombstone with a new generation so setups that began earlier cannot recreate
    /// it. Returns <c>true</c> when an enrolment was active.
    /// </summary>
    Task<bool> DeleteAsync(
        Guid tenantId,
        string normalizedEmail,
        DateTime removedAtUtc,
        CancellationToken cancellationToken = default);
}

public sealed record TestAuthAuthenticatorEnrolment(byte[] Secret, DateTime CreatedAtUtc);
