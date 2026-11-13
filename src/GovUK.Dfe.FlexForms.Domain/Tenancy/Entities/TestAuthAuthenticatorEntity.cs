namespace GovUK.Dfe.FlexForms.Domain.Tenancy.Entities;

/// <summary>
/// An authenticator app (TOTP) a user enrolled for Test Authentication sign-in in one tenant.
/// Keyed by tenant and normalised email because Test Authentication identifies users by email before token exchange.
/// </summary>
public class TestAuthAuthenticatorEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>Trimmed, lower-case email address.</summary>
    public string NormalizedEmail { get; set; } = string.Empty;

    /// <summary>
    /// The TOTP secret, protected with Data Protection. Never stored or logged in plaintext. Empty once removed.
    /// </summary>
    public string EncryptedSecret { get; set; } = string.Empty;

    /// <summary>
    /// Generation of this tenant/email: the id of the setup that produced the enrolment, or a fresh id when it was
    /// removed. A concurrency token: saves and removals only apply to the generation they read, so a delayed
    /// confirmation cannot overwrite a newer enrolment or recreate a removed one.
    /// </summary>
    public string SetupId { get; set; } = string.Empty;

    /// <summary>
    /// Set when the user removed their authenticator. The row is kept as a tombstone (secret wiped) so its
    /// <see cref="SetupId"/> still guards against setups that began before the removal.
    /// </summary>
    public DateTime? RemovedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? LastUsedAtUtc { get; set; }

    public TenantEntity? Tenant { get; set; }
}
