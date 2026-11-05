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
    /// Required per-tenant file storage. Host DI uses GlobalConfiguration:FileStorage only to
    /// boot CoreLibs (can be Local with a dummy path). Azure ConnectionString/ShareName are read
    /// from this tenant row at runtime. Must not be edited by Tenant Admins.
    /// </summary>
    public const string FileStorage = "FileStorage";

    /// <summary>
    /// Required per-tenant email (Notify keys, support address). Host registration uses
    /// GlobalConfiguration:Email; runtime sends require this tenant row and do not fall back to host.
    /// </summary>
    public const string Email = "Email";

    /// <summary>
    /// Per-tenant Application Insights connection string. Runtime telemetry
    /// (and the Web JS snippet) use this tenant row, falling back to each app's own
    /// host connection string if unset (API: GlobalConfiguration; Web: its own appsettings).
    /// </summary>
    public const string ApplicationInsights = "ApplicationInsights";

    /// <summary>
    /// Categories restricted to SuperAdmin. Extend this set when locking more settings.
    /// </summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ApplicationTemplates,
        Template,
        ConnectionStrings,
        FileStorage,
        Email,
        ApplicationInsights
    };

    public static bool IsRestricted(string? category) =>
        !string.IsNullOrWhiteSpace(category) && All.Contains(category.Trim());
}
