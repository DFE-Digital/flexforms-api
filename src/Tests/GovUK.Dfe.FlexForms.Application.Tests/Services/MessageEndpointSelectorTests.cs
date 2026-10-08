using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Events;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Utils.Configuration;
using MassTransit;
using MassTransit.DependencyInjection;
using MassTransit.Middleware;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class MessageEndpointSelectorTests
{
    private readonly IPublishEndpoint _scopedPublish = Substitute.For<IPublishEndpoint>();
    private readonly ISendEndpointProvider _scopedSend = Substitute.For<ISendEndpointProvider>();
    private readonly IBus _bus = Substitute.For<IBus>();

    private MessageEndpointSelector CreateSelector(
        OutboxOptions options,
        bool scopedIsOutbox,
        Dictionary<string, string?>? tenantSettings = null)
    {
        var context = scopedIsOutbox
            ? Substitute.For<ScopedBusContext, OutboxSendContext>()
            : Substitute.For<ScopedBusContext>();

        var contextProvider = Substitute.For<IScopedBusContextProvider<IBus>>();
        contextProvider.Context.Returns(context);

        var tenantAccessor = Substitute.For<ITenantContextAccessor>();
        if (tenantSettings is not null)
        {
            tenantAccessor.CurrentTenant.Returns(new TenantConfiguration(
                Guid.NewGuid(),
                "Tenant",
                new ConfigurationBuilder().AddInMemoryCollection(tenantSettings).Build(),
                []));
        }

        return new MessageEndpointSelector(
            _scopedPublish,
            _scopedSend,
            _bus,
            contextProvider,
            Microsoft.Extensions.Options.Options.Create(options),
            tenantAccessor);
    }

    [Fact]
    public void GetSendEndpointProvider_UsesTenantAllowlist_OverHostAllowlist()
    {
        var selector = CreateSelector(
            new OutboxOptions { Events = ["host-topic"] },
            scopedIsOutbox: true,
            new Dictionary<string, string?> { ["MassTransit:Outbox:Events:0"] = "tenant-topic" });

        Assert.Same(_scopedSend, selector.GetSendEndpointProvider("TenantSchemaEvent", "tenant-topic"));
        Assert.Same(_bus, selector.GetSendEndpointProvider("HostSchemaEvent", "host-topic"));
    }

    [Fact]
    public void GetPublishEndpoint_KeepsPrismEventsOnOutbox_WhenTenantOptsOut()
    {
        var selector = CreateSelector(
            new OutboxOptions { Mode = OutboxRoutingMode.All },
            scopedIsOutbox: true,
            new Dictionary<string, string?> { ["MassTransit:Outbox:Enabled"] = "false" });

        Assert.Same(_bus, selector.GetPublishEndpoint(typeof(TestEvent)));
        Assert.Same(_scopedPublish, selector.GetPublishEndpoint(typeof(ApplicationProjectionRequestedEvent)));
    }

    [Fact]
    public void GetPublishEndpoint_ReturnsOutboxEndpoint_WhenEventIsAllowlisted()
    {
        var selector = CreateSelector(new OutboxOptions { Events = ["TestEvent"] }, scopedIsOutbox: true);

        Assert.Same(_scopedPublish, selector.GetPublishEndpoint(typeof(TestEvent)));
    }

    [Fact]
    public void GetPublishEndpoint_BypassesOutbox_WhenEventIsNotAllowlisted()
    {
        var selector = CreateSelector(new OutboxOptions { Events = ["SomethingElse"] }, scopedIsOutbox: true);

        Assert.Same(_bus, selector.GetPublishEndpoint(typeof(TestEvent)));
    }

    [Fact]
    public void GetPublishEndpoint_UsesOutboxForEverything_InAllMode()
    {
        var selector = CreateSelector(new OutboxOptions { Mode = OutboxRoutingMode.All }, scopedIsOutbox: true);

        Assert.Same(_scopedPublish, selector.GetPublishEndpoint(typeof(TestEvent)));
    }

    [Fact]
    public void GetPublishEndpoint_KeepsScopedEndpoint_WhenItIsNotOutboxBacked()
    {
        // e.g. inside a consumer, where the scoped endpoint wraps the consume context.
        var selector = CreateSelector(new OutboxOptions(), scopedIsOutbox: false);

        Assert.Same(_scopedPublish, selector.GetPublishEndpoint(typeof(TestEvent)));
    }

    [Fact]
    public void GetSendEndpointProvider_MatchesSchemaEventByTopicName()
    {
        var selector = CreateSelector(new OutboxOptions { Events = ["prism-topic"] }, scopedIsOutbox: true);

        Assert.Same(_scopedSend, selector.GetSendEndpointProvider("PrismEvent", "prism-topic"));
        Assert.Same(_bus, selector.GetSendEndpointProvider("OtherEvent", "other-topic"));
    }

    [Theory]
    [InlineData(typeof(ApplicationProjectionRequestedEvent))]
    [InlineData(typeof(TemplateVersionPublishedEvent))]
    public void GetPublishEndpoint_AlwaysUsesOutbox_ForPrismEvents(Type eventType)
    {
        var selector = CreateSelector(new OutboxOptions { Events = [] }, scopedIsOutbox: true);

        Assert.Same(_scopedPublish, selector.GetPublishEndpoint(eventType));
    }

    [Fact]
    public void GetPublishEndpoint_AlwaysUsesOutbox_ForPrismProjectionEvents_EvenWhenOutboxDisabled()
    {
        var selector = CreateSelector(new OutboxOptions { Enabled = false }, scopedIsOutbox: true);

        Assert.Same(_scopedPublish, selector.GetPublishEndpoint(typeof(ApplicationProjectionRequestedEvent)));
    }

    public record TestEvent;
}
