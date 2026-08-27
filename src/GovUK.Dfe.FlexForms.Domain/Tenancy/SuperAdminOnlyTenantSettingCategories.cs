namespace GovUK.Dfe.FlexForms.Domain.Tenancy;

/// <summary>
/// TenantConfig categories that only interactive SuperAdmins may create, update, or delete.
/// Tenant Admins may still view them when listed (secrets remain redacted by the settings API).
/// </summary>
public static class SuperAdminOnlyTenantSettingCategories
{
    /// <summary>API HostMappings / catalogue template GUIDs.</summary>
    public const string ApplicationTemplates = "ApplicationTemplates";

    /// <summary>Web HostMappings / default template Id.</summary>
    public const string Template = "Template";

    /// <summary>Tenant database connection strings.</summary>
    public const string ConnectionStrings = "ConnectionStrings";

    /// <summary>
    /// Per-tenant file storage overlay. Host DI requires GlobalConfiguration:FileStorage;
    /// tenant rows are optional runtime overrides and must not be edited by Tenant Admins.
    /// </summary>
    public const string FileStorage = "FileStorage";

    /// <summary>
    /// Per-tenant email overlay (Notify keys, support address). Host registration should use
    /// GlobalConfiguration:Email in non-local environments.
    /// </summary>
    public const string Email = "Email";

    /// <summary>
    /// Categories restricted to SuperAdmin. Extend this set when locking more settings.
    /// </summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ApplicationTemplates,
        Template,
        ConnectionStrings,
        FileStorage,
        Email
    };

    public static bool IsRestricted(string? category) =>
        !string.IsNullOrWhiteSpace(category) && All.Contains(category.Trim());
}
