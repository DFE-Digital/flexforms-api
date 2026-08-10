namespace GovUK.Dfe.FlexForms.Application.Options;

/// <summary>
/// Tenant-configured outbound events per lifecycle trigger.
/// Bound from the <c>EventTriggers</c> TenantConfig category, keyed by
/// <see cref="EventTriggerType"/> name.
/// </summary>
public class EventTriggersOptions : Dictionary<string, List<EventEntryOptions>>
{
    public const string SectionName = "EventTriggers";

    public EventTriggersOptions()
        : base(StringComparer.OrdinalIgnoreCase)
    {
    }
}
