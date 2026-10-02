using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Events;

namespace GovUK.Dfe.FlexForms.Application.Messaging;

/// <summary>
/// CoreLibs messaging events that only the platform itself publishes. They are hidden from the
/// tenant event catalogue and EventTrigger discovery so a tenant can never publish onto their topics.
/// </summary>
public static class PlatformInternalMessagingEvents
{
    private static readonly HashSet<string> Names = new(StringComparer.Ordinal)
    {
        nameof(ApplicationProjectionRequestedEvent),
        nameof(TemplateVersionPublishedEvent)
    };

    public static bool IsInternal(Type type) => Names.Contains(type.Name);
}
