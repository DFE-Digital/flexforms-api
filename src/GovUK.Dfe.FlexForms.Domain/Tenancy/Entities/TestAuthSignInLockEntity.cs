namespace GovUK.Dfe.FlexForms.Domain.Tenancy.Entities;

/// <summary>
/// An administrator's lock on a user's Test Authentication sign-in in one tenant. While the row exists every Test
/// Authentication sign-in method (emailed one-time password, authenticator app, setup code) is refused for that email.
/// It never expires: only an administrator removes it.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="TestAuthAuthenticatorEntity"/> on purpose: enrolling, replacing or removing an
/// authenticator rewrites that row, and must never clear or change a lock.
/// </remarks>
public class TestAuthSignInLockEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>Trimmed, lower-case email address.</summary>
    public string NormalizedEmail { get; set; } = string.Empty;

    public DateTime LockedAtUtc { get; set; }

    /// <summary>Email of the administrator who set the lock.</summary>
    public string LockedByEmail { get; set; } = string.Empty;

    public TenantEntity? Tenant { get; set; }
}
