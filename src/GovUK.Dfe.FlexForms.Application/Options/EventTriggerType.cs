namespace GovUK.Dfe.FlexForms.Application.Options;

/// <summary>
/// Points in the application lifecycle that can fan out tenant-configured outbound events.
/// Trigger names are the keys of the <c>EventTriggers</c> TenantConfig category.
/// </summary>
public static class EventTriggerType
{
    /// <summary>Raised after an application has been submitted.</summary>
    public const string ApplicationSubmitted = "ApplicationSubmitted";

    /// <summary>Raised after a file has been uploaded and queued for virus scanning.</summary>
    public const string FileUploaded = "FileUploaded";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ApplicationSubmitted,
        FileUploaded
    };

    public static bool IsKnown(string? trigger) =>
        !string.IsNullOrWhiteSpace(trigger) && All.Contains(trigger.Trim());
}
