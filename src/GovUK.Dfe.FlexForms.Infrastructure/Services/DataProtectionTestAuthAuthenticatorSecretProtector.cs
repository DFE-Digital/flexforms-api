using System.Security.Cryptography;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.AspNetCore.DataProtection;

namespace GovUK.Dfe.FlexForms.Infrastructure.Services;

/// <summary>
/// Protects authenticator app secrets with the shared Data Protection key ring under their own purpose, so they
/// cannot be decrypted as tenant settings or vice versa.
/// </summary>
public sealed class DataProtectionTestAuthAuthenticatorSecretProtector(IDataProtectionProvider dataProtectionProvider)
    : ITestAuthAuthenticatorSecretProtector
{
    private const string Purpose = "TestAuthAuthenticator.v1";

    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(Purpose);

    public string Protect(byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return Convert.ToBase64String(_protector.Protect(secret));
    }

    public byte[]? Unprotect(string protectedSecret)
    {
        try
        {
            return _protector.Unprotect(Convert.FromBase64String(protectedSecret));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
