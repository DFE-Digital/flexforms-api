using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Events;
using GovUK.Dfe.FlexForms.Application.Messaging;

namespace GovUK.Dfe.FlexForms.Application.Tests.Messaging;

public class PlatformInternalMessagingEventsTests
{
    [Theory]
    [InlineData(typeof(ApplicationProjectionRequestedEvent))]
    [InlineData(typeof(TemplateVersionPublishedEvent))]
    public void Prism_event_is_hidden_from_tenant_event_catalogue(Type eventType)
    {
        var catalogue = PlatformEventCatalogueBuilder.Build();

        Assert.DoesNotContain(catalogue.Events, e => e.EventTypeName == eventType.Name);
        Assert.Contains(catalogue.Events, e => e.EventTypeName == nameof(TransferApplicationSubmittedEvent));
    }

    [Theory]
    [InlineData(typeof(ApplicationProjectionRequestedEvent))]
    [InlineData(typeof(TemplateVersionPublishedEvent))]
    public void Prism_event_is_not_discovered_for_event_triggers(Type eventType)
    {
        var discovered = MessagingEventDiscovery.Discover();

        Assert.DoesNotContain(discovered, e => e.ClrType == eventType);
        Assert.Contains(discovered, e => e.ClrType == typeof(TransferApplicationSubmittedEvent));
    }
}
