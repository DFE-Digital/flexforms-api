using Microsoft.Extensions.Hosting;

namespace GovUK.Dfe.FlexForms.Application.Security;

/// <summary>
/// Controls when decrypted tenant-setting secrets may leave admin list/validate APIs.
/// Fail closed: Production (and unknown / unset environments) never return plaintext.
/// Dev and Test allow interactive SuperAdmin only — tenant Admin stays redacted.
/// </summary>
public static class TenantSettingSecretPlaintextGate
{
    private static readonly HashSet<string> PlaintextEnvironments = new(StringComparer.OrdinalIgnoreCase)
    {
        Environments.Development,
        "Local",
        "Dev",
        "Test",
        "Testing"
    };

    /// <summary>
    /// Returns <c>true</c> when this host environment may return plaintext secrets
    /// to an interactive SuperAdmin.
    /// </summary>
    public static bool IsPlaintextEnvironment(IHostEnvironment? environment)
        => IsPlaintextEnvironment(environment?.EnvironmentName);

    /// <summary>
    /// Returns <c>true</c> for Development / Local / Dev / Test / Testing only.
    /// Empty, Staging, Production, and unknown names are denied.
    /// </summary>
    public static bool IsPlaintextEnvironment(string? environmentName)
    {
        if (string.IsNullOrWhiteSpace(environmentName))
        {
            return false;
        }

        if (string.Equals(environmentName, Environments.Production, StringComparison.OrdinalIgnoreCase)
            || string.Equals(environmentName, "Prod", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return PlaintextEnvironments.Contains(environmentName.Trim());
    }

    /// <summary>
    /// SuperAdmin plaintext is allowed only in Dev/Test-class environments.
    /// </summary>
    public static bool AllowsSuperAdminPlaintext(IHostEnvironment? environment, bool isInteractiveSuperAdmin)
        => isInteractiveSuperAdmin && IsPlaintextEnvironment(environment);
}
