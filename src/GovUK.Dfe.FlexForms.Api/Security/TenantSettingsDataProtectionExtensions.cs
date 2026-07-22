using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;

namespace GovUK.Dfe.FlexForms.Api.Security;

/// <summary>
/// Registers Data Protection for encrypting and decrypting secret TenantSettings.
/// </summary>
public static class TenantSettingsDataProtectionExtensions
{
    /// <summary>
    /// Configures Data Protection for TenantSettings encryption.
    /// Local and Development environments always use the default local key ring.
    /// Azure Blob + Key Vault are used only when <see cref="DataProtectionSettings.UseAzure"/>
    /// is true outside those environments.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="environment">Hosting environment (Local/Development force local keys).</param>
    /// <returns>The Data Protection builder for further chaining if needed.</returns>
    public static IDataProtectionBuilder AddTenantSettingsDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var settings = configuration
            .GetSection(DataProtectionSettings.SectionName)
            .Get<DataProtectionSettings>()
            ?? new DataProtectionSettings();

        // Local/Development: default key ring only (no SetApplicationName) so existing
        // locally encrypted TenantSettings remain decryptable.
        var builder = services.AddDataProtection();

        if (ShouldUseLocalKeyRing(environment, settings))
            return builder;

        var applicationName = string.IsNullOrWhiteSpace(settings.ApplicationName)
            ? "GovUK.Dfe.FlexForms.Api"
            : settings.ApplicationName;

        builder.SetApplicationName(applicationName);

        if (string.IsNullOrWhiteSpace(settings.BlobUri))
        {
            throw new InvalidOperationException(
                "DataProtection:BlobUri is required when DataProtection:UseAzure is true.");
        }

        if (string.IsNullOrWhiteSpace(settings.KeyVaultKeyId))
        {
            throw new InvalidOperationException(
                "DataProtection:KeyVaultKeyId is required when DataProtection:UseAzure is true.");
        }

        var credential = new DefaultAzureCredential();

        return builder
            .PersistKeysToAzureBlobStorage(new Uri(settings.BlobUri), credential)
            .ProtectKeysWithAzureKeyVault(new Uri(settings.KeyVaultKeyId), credential);
    }

    private static bool ShouldUseLocalKeyRing(
        IHostEnvironment environment,
        DataProtectionSettings settings)
    {
        // launchSettings Local profiles must keep working without Azure resources.
        if (environment.IsEnvironment("Local")) return true;

        return !settings.UseAzure;
    }
}
