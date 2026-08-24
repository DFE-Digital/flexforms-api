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
    /// Categories restricted to SuperAdmin. Extend this set when locking more settings.
    /// </summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ApplicationTemplates,
        Template,
        ConnectionStrings
    };

    public static bool IsRestricted(string? category) =>
        !string.IsNullOrWhiteSpace(category) && All.Contains(category.Trim());
}
