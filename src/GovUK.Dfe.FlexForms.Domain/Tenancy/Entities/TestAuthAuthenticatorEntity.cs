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

    /// <summary>The TOTP secret, protected with Data Protection. Never stored or logged in plaintext.</summary>
    public string EncryptedSecret { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? LastUsedAtUtc { get; set; }

    public TenantEntity? Tenant { get; set; }
}
