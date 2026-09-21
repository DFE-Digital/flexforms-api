namespace GovUK.Dfe.FlexForms.Api.Security;

/// <summary>
/// Configuration for ASP.NET Core Data Protection used to encrypt secret TenantSettings rows.
/// </summary>
public sealed class DataProtectionSettings
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "DataProtection";

    /// <summary>
    /// When true, persists the key ring to Azure Blob Storage and protects it with an Azure Key Vault key.
    /// When false, uses the default local key ring (typical for local development without Azure).
    /// </summary>
    public bool UseAzure { get; set; }

    /// <summary>
    /// When true (and <see cref="UseAzure"/> is true), authenticates to blob storage using the SAS
    /// query string embedded in <see cref="BlobUri"/> instead of managed identity.
    /// Key Vault wrapping still uses <see cref="Azure.Identity.DefaultAzureCredential"/> (managed identity / local Azure CLI login).
    /// </summary>
    public bool UseStorageSas { get; set; }

    /// <summary>
    /// Stable application name for the Data Protection key ring.
    /// Do not change after secret TenantSettings have been encrypted.
    /// Required when <see cref="UseAzure"/> is set, where it defaults to
    /// <c>GovUK.Dfe.FlexForms.Api</c>. With a local key ring it is optional: leave it empty to keep
    /// the Data Protection default, and set it only where a key ring is shared between machines
    /// that would otherwise derive different names (the API container and its host).
    /// </summary>
    public string ApplicationName { get; set; } = string.Empty;

    /// <summary>
    /// Full blob URI for the shared key-ring XML.
    /// With managed identity: https://account.blob.core.windows.net/container/api-keys.xml
    /// With <see cref="UseStorageSas"/>: same URI plus SAS query string (?sp=...&amp;sig=...).
    /// </summary>
    public string? BlobUri { get; set; }

    /// <summary>
    /// Key Vault key identifier used to wrap the Data Protection key ring
    /// (e.g. https://vault.vault.azure.net/keys/tenant-settings-dp).
    /// Always accessed with managed identity / DefaultAzureCredential.
    /// </summary>
    public string? KeyVaultKeyId { get; set; }

    /// <summary>
    /// Directory for the local file-system key ring (for example
    /// <c>/home/app/.aspnet/DataProtection-Keys</c> in the API container, where the host key
    /// directory is bind-mounted). <c>%LOCALAPPDATA%</c>, <c>$HOME</c> and <c>${HOME}</c> style
    /// variables are expanded.
    /// Leave empty — the default — to use the ASP.NET default key location, which is
    /// <c>%LOCALAPPDATA%\ASP.NET\DataProtection-Keys</c> on Windows and
    /// <c>$HOME/.aspnet/DataProtection-Keys</c> elsewhere. A path that cannot be used on the
    /// current machine (a container path when running directly on Windows, a variable that is not
    /// set, or a relative path) also falls back to that default.
    /// </summary>
    public string LocalKeysPath { get; set; } = string.Empty;
}
