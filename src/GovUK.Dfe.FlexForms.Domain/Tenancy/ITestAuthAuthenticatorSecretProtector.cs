namespace GovUK.Dfe.FlexForms.Domain.Tenancy;

/// <summary>
/// Encrypts Test Authentication authenticator app secrets wherever they are stored (database or Redis).
/// </summary>
public interface ITestAuthAuthenticatorSecretProtector
{
    string Protect(byte[] secret);

    /// <summary>Returns <c>null</c> when the value cannot be decrypted (for example after a key ring loss).</summary>
    byte[]? Unprotect(string protectedSecret);
}
