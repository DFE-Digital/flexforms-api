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
    /// </summary>
    public string ApplicationName { get; set; } = "GovUK.Dfe.FlexForms.Api";

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
    /// <c>/home/app/.aspnet/DataProtection-Keys</c> in the API container).
    /// Bind-mount the host key directory to this path (read-only is fine).
    /// When the path is not writable, XML keys are copied to a temp directory
    /// and automatic key generation is disabled so the container only decrypts.
    /// Leave empty to use the ASP.NET default key location.
    /// </summary>
    public string LocalKeysPath { get; set; } = "/home/app/.aspnet/DataProtection-Keys";
}
