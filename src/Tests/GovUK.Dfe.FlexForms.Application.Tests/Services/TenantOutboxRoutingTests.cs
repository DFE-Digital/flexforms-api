using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Utils.Configuration;
using Microsoft.Extensions.Configuration;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class TenantOutboxRoutingTests
{
    private static TenantConfiguration Tenant(Dictionary<string, string?> settings) =>
        new(Guid.NewGuid(), "Tenant", new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), []);

    [Fact]
    public void Resolve_ReturnsHostOptions_WhenTenantHasNoOutboxSettings()
    {
        var host = new OutboxOptions { Events = ["HostEvent"] };

        Assert.Same(host, TenantOutboxRouting.Resolve(host, Tenant([])));
        Assert.Same(host, TenantOutboxRouting.Resolve(host, null));
    }

    [Fact]
    public void Resolve_TenantEventsReplaceHostEvents()
    {
        var host = new OutboxOptions { Events = ["HostEvent"] };
        var tenant = Tenant(new()
        {
            ["MassTransit:Outbox:Events:0"] = "tenant-topic",
            ["MassTransit:Outbox:Events:1"] = " TenantEvent "
        });

        var resolved = TenantOutboxRouting.Resolve(host, tenant);

        Assert.Equal(["tenant-topic", "TenantEvent"], resolved.Events);
        Assert.False(resolved.UsesOutbox("HostEvent"));
        Assert.True(resolved.UsesOutbox("TENANT-TOPIC"));
        Assert.Equal(["HostEvent"], host.Events);
    }

    [Fact]
    public void Resolve_AcceptsSingleStringEvents()
    {
        var resolved = TenantOutboxRouting.Resolve(
            new OutboxOptions(),
            Tenant(new() { ["MassTransit:Outbox:Events"] = "tenant-topic" }));

        Assert.Equal(["tenant-topic"], resolved.Events);
    }

    [Fact]
    public void Resolve_InheritsHostEvents_WhenTenantOnlySetsMode()
    {
        var host = new OutboxOptions { Mode = OutboxRoutingMode.All, Events = ["HostEvent"] };

        var resolved = TenantOutboxRouting.Resolve(
            host,
            Tenant(new() { ["MassTransit:Outbox:Mode"] = "allowlist" }));

        Assert.Equal(OutboxRoutingMode.Allowlist, resolved.Mode);
        Assert.Equal(["HostEvent"], resolved.Events);
    }

    [Fact]
    public void Resolve_TenantCanOptOut()
    {
        var resolved = TenantOutboxRouting.Resolve(
            new OutboxOptions { Mode = OutboxRoutingMode.All },
            Tenant(new() { ["MassTransit:Outbox:Enabled"] = "false" }));

        Assert.False(resolved.UsesOutbox("AnyEvent"));
    }

    [Fact]
    public void Resolve_TenantCannotEnable_WhenHostDisabled()
    {
        var resolved = TenantOutboxRouting.Resolve(
            new OutboxOptions { Enabled = false },
            Tenant(new()
            {
                ["MassTransit:Outbox:Enabled"] = "true",
                ["MassTransit:Outbox:Events:0"] = "TenantEvent"
            }));

        Assert.False(resolved.UsesOutbox("TenantEvent"));
    }
}
