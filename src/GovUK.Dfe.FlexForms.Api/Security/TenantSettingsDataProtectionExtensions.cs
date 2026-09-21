using System.Text.RegularExpressions;
using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;

namespace GovUK.Dfe.FlexForms.Api.Security;

/// <summary>
/// Registers Data Protection for encrypting and decrypting secret TenantSettings.
/// </summary>
public static partial class TenantSettingsDataProtectionExtensions
{
    [GeneratedRegex(@"\$(?:\{(?<name>[^}]+)\}|(?<name>[A-Za-z_][A-Za-z0-9_]*))")]
    private static partial Regex ShellVariableRegex();

    /// <summary>
    /// Configures Data Protection for TenantSettings encryption.
    /// When Azure is not used, registers the default local key ring.
    /// When Azure is used, persists keys to Blob Storage (managed identity, or SAS when
    /// <see cref="DataProtectionSettings.UseStorageSas"/> is true) and protects them with
    /// Key Vault via <see cref="DefaultAzureCredential"/> (managed identity / Azure CLI).
    /// </summary>
    public static IDataProtectionBuilder AddTenantSettingsDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var settings = configuration
            .GetSection(DataProtectionSettings.SectionName)
            .Get<DataProtectionSettings>()
            ?? new DataProtectionSettings();

        var builder = services.AddDataProtection();

        // Local/Development without Azure opt-in: file-system key ring.
        if (ShouldUseLocalKeyRing(environment, settings))
        {
            // Unlike Azure, an application name is opt-in here: the container sets one so it shares
            // an identity with the bind-mounted host key ring, while a plain "run from the IDE" keeps
            // the Data Protection default so TenantSettings encrypted before that are still readable.
            if (!string.IsNullOrWhiteSpace(settings.ApplicationName))
            {
                builder.SetApplicationName(settings.ApplicationName);
            }

            var localKeysPath = ResolveLocalKeysPath(settings.LocalKeysPath);
            if (localKeysPath is not null)
            {
                builder.PersistKeysToFileSystem(new DirectoryInfo(localKeysPath));
            }

            Console.WriteLine(
                $"Using local key ring for Data Protection (no Azure): {localKeysPath ?? "Data Protection default location"}");

            return builder;
        }

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

        if (!Uri.TryCreate(settings.BlobUri, UriKind.Absolute, out var blobUri))
        {
            throw new InvalidOperationException(
                "DataProtection:BlobUri must be an absolute URI.");
        }

        if (!Uri.TryCreate(settings.KeyVaultKeyId, UriKind.Absolute, out var keyVaultKeyUri))
        {
            throw new InvalidOperationException(
                "DataProtection:KeyVaultKeyId must be an absolute URI.");
        }

        // Key Vault: managed identity in Azure; Azure CLI / VS login locally (never probe IMDS when using SAS).
        var credential = CreateKeyVaultCredential(environment, settings);

        if (settings.UseStorageSas)
        {
            if (string.IsNullOrWhiteSpace(blobUri.Query) || blobUri.Query.Length <= 1)
            {
                throw new InvalidOperationException(
                    "DataProtection:BlobUri must include a SAS query string when DataProtection:UseStorageSas is true. " +
                    "Example: https://account.blob.core.windows.net/container/api-keys.xml?sp=rw&st=...&sig=...");
            }

            // Uri-only overload authenticates with the SAS embedded in BlobUri — no storage MI.
            builder.PersistKeysToAzureBlobStorage(blobUri);
        }
        else
        {
            builder.PersistKeysToAzureBlobStorage(blobUri, credential);
        }

        return builder.ProtectKeysWithAzureKeyVault(keyVaultKeyUri, credential);
    }

    /// <summary>
    /// Builds the credential used for Key Vault (and for blob when not using SAS).
    /// When <see cref="DataProtectionSettings.UseStorageSas"/> is set (typical local Azure opt-in),
    /// managed identity / IMDS is excluded so DefaultAzureCredential uses Azure CLI or Visual Studio login.
    /// </summary>
    private static DefaultAzureCredential CreateKeyVaultCredential(
        IHostEnvironment environment,
        DataProtectionSettings settings)
    {
        var useDeveloperCredentials =
            settings.UseStorageSas || environment.IsEnvironment("Local");

        if (!useDeveloperCredentials)
            return new DefaultAzureCredential();

        return new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            // Avoid IMDS probes (169.254.169.254) that fail slowly / hard on developer machines.
            ExcludeManagedIdentityCredential = true,
            ExcludeWorkloadIdentityCredential = true,
            ExcludeEnvironmentCredential = false,
            ExcludeAzureCliCredential = false,
            ExcludeVisualStudioCredential = false,
            ExcludeAzurePowerShellCredential = false,
            ExcludeInteractiveBrowserCredential = true
        });
    }

    /// <summary>
    /// Resolves <see cref="DataProtectionSettings.LocalKeysPath"/> to a directory usable on the
    /// current machine, or <c>null</c> to let Data Protection choose its own default location
    /// (<c>%LOCALAPPDATA%\ASP.NET\DataProtection-Keys</c> on Windows,
    /// <c>$HOME/.aspnet/DataProtection-Keys</c> elsewhere).
    /// </summary>
    private static string? ResolveLocalKeysPath(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return null;

        var path = ExpandVariables(configuredPath.Trim());

        // The container path ("/home/app/.aspnet/DataProtection-Keys") silently resolves to
        // "C:\home\app\.aspnet\DataProtection-Keys" when the API runs directly on Windows, and a
        // Windows path is meaningless in the container. Either way a brand new key ring would be
        // created and existing secrets could no longer be decrypted, so use the platform default.
        if (!IsUsableOnCurrentPlatform(path))
            return null;

        return Path.GetFullPath(path);
    }

    /// <summary>
    /// Expands both <c>%WINDOWS%</c> and <c>$SHELL</c> / <c>${SHELL}</c> style variables so the
    /// setting can be written in whichever form matches the developer's machine.
    /// </summary>
    private static string ExpandVariables(string value) =>
        ShellVariableRegex().Replace(
            Environment.ExpandEnvironmentVariables(value),
            match => Environment.GetEnvironmentVariable(match.Groups["name"].Value) ?? match.Value);

    private static bool IsUsableOnCurrentPlatform(string path) =>
        // A leftover token means the variable is not set here; a relative path would land in the
        // working directory rather than anywhere a key ring is expected.
        !path.Contains('%')
        && !path.Contains('$')
        && Path.IsPathFullyQualified(path);

    private static bool ShouldUseLocalKeyRing(
        IHostEnvironment environment,
        DataProtectionSettings settings)
    {
        if (!settings.UseAzure)
            return true;

        // Local launch profiles often inherit UseAzure=true from appsettings.json.
        // Keep the local key ring unless the developer explicitly opts into Azure blob access
        // via UseStorageSas (SAS URL in BlobUri + Key Vault via DefaultAzureCredential).
        if (environment.IsEnvironment("Local") && !settings.UseStorageSas)
            return true;

        return false;
    }
}
