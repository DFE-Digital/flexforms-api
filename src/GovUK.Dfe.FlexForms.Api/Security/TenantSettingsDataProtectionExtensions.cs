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
    /// The Local environment always uses the default local key ring. All other environments
    /// (including the deployed Azure "Test" environment) follow <see cref="DataProtectionSettings.UseAzure"/>.
    /// Integration test hosts force local keys by setting DataProtection:UseAzure=false rather than
    /// relying on an environment name.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="environment">Hosting environment (only "Local" forces local keys).</param>
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
        // Local (launch profiles) never require Azure.
        // NOTE: do NOT special-case "Test" by environment name - there is a deployed Azure
        // environment named "Test" that must use Azure DP. Integration test hosts (which run as
        // ASPNETCORE_ENVIRONMENT/UseEnvironment "Test") instead set DataProtection:UseAzure=false,
        // so they fall through to the local key ring via the UseAzure flag below.
        if (environment.IsEnvironment("Local"))
            return true;

        return !settings.UseAzure;
    }
}
