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
    public const string EventTriggers = "EventTriggers";

    /// <summary>Target used for safe settings that only the Web app reads.</summary>
    public const string DefaultTarget = "Web";

    /// <summary>Target used for safe settings the API reads at runtime as well as the Web admin UI.</summary>
    public const string SharedTarget = "Shared";

    /// <summary>
    /// Categories the API runtime must be able to read, so they are stored against
    /// <see cref="SharedTarget"/> rather than the Web-only target.
    /// </summary>
    private static readonly IReadOnlySet<string> SharedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        EventMappings,
        SchemaEvents,
        EventTriggers
    };

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ApplicationTerminology,
        NotificationBanner,
        Dashboard,
        EventMappings,
        SchemaEvents,
        EventTriggers
    };

    public static bool IsSafe(string? category) =>
        !string.IsNullOrWhiteSpace(category) && All.Contains(category.Trim());

    /// <summary>
    /// Returns the target a delegated setting must be persisted under.
    /// </summary>
    public static string TargetFor(string? category) =>
        !string.IsNullOrWhiteSpace(category) && SharedCategories.Contains(category.Trim())
            ? SharedTarget
            : DefaultTarget;
}
