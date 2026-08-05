namespace GovUK.Dfe.FlexForms.Domain.Tenancy;

/// <summary>
/// Non-secret TenantConfig categories that tenant Admins may view and edit
/// without SuperAdmin access to encrypted secrets.
/// </summary>
public static class TenantSafeSettingCategories
{
    public const string ApplicationTerminology = "ApplicationTerminology";
    public const string NotificationBanner = "NotificationBanner";
    public const string Dashboard = "Dashboard";
    public const string EventMappings = "EventMappings";
    public const string SchemaEvents = "SchemaEvents";

    /// <summary>Target used for all delegated safe settings (Web app options).</summary>
    public const string DefaultTarget = "Web";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ApplicationTerminology,
        NotificationBanner,
        Dashboard,
        EventMappings,
        SchemaEvents
    };

    public static bool IsSafe(string? category) =>
        !string.IsNullOrWhiteSpace(category) && All.Contains(category.Trim());
}
