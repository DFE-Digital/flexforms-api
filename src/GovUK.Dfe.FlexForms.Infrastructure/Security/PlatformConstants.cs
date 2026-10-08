namespace GovUK.Dfe.FlexForms.Infrastructure.Security;

/// <summary>
/// Constants for platform-level API access (host config bootstrap, tenant config by id, etc.).
/// </summary>
public static class PlatformConstants
{
    /// <summary>Authorization policy for reading global host configuration.</summary>
    public const string PlatformHostPolicy = "PlatformHost";

    /// <summary>Entra app role required to call host-config endpoints.</summary>
    public const string HostReadAppRole = "Platform.Host.Read";

    /// <summary>Authorization policy for reading tenant configuration by tenant id or hostname.</summary>
    public const string PlatformTenantConfigPolicy = "PlatformTenantConfig";

    /// <summary>Entra app role required to call platform tenant-config endpoints.</summary>
    public const string TenantConfigReadAppRole = "Platform.TenantConfig.Read";

    /// <summary>Authorization policy for the internal read endpoints used by the Prism analytics projector.</summary>
    public const string PlatformPrismReadPolicy = "PlatformPrismRead";

    /// <summary>Entra app role required to call the internal Prism endpoints.</summary>
    public const string PrismReadAppRole = "Prism.Read";

    /// <summary>Configuration section for the API's own Entra app registration (platform tokens).</summary>
    public const string AzureAdSection = "Platform:AzureAd";
}
