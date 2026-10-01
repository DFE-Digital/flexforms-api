using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Events;
using GovUK.Dfe.FlexForms.Application.Messaging;

namespace GovUK.Dfe.FlexForms.Application.Tests.Messaging;

public class PlatformInternalMessagingEventsTests
{
    [Fact]
    public void Prism_projection_event_is_hidden_from_tenant_event_catalogue()
    {
        var catalogue = PlatformEventCatalogueBuilder.Build();

        Assert.DoesNotContain(catalogue.Events, e => e.EventTypeName == nameof(ApplicationProjectionRequestedEvent));
        Assert.Contains(catalogue.Events, e => e.EventTypeName == nameof(TransferApplicationSubmittedEvent));
    }

    [Fact]
    public void Prism_projection_event_is_not_discovered_for_event_triggers()
    {
        var discovered = MessagingEventDiscovery.Discover();

        Assert.DoesNotContain(discovered, e => e.ClrType == typeof(ApplicationProjectionRequestedEvent));
        Assert.Contains(discovered, e => e.ClrType == typeof(TransferApplicationSubmittedEvent));
    }
}
