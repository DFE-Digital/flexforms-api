using GovUK.Dfe.FlexForms.Utils.Configuration;
using Microsoft.Extensions.Configuration;

namespace GovUK.Dfe.FlexForms.Application.Tests.Configuration;

public class OutboxOptionsTests
{
    [Fact]
    public void Defaults_RouteNothingThroughTheOutbox()
    {
        var options = new OutboxOptions();

        Assert.Equal(OutboxRoutingMode.Allowlist, options.Mode);
        Assert.False(options.UsesOutbox("ScanRequestedEvent"));
    }

    [Fact]
    public void UsesOutbox_MatchesAllowlistCaseInsensitively()
    {
        var options = new OutboxOptions { Events = ["ScanRequestedEvent"] };

        Assert.True(options.UsesOutbox("scanrequestedevent"));
        Assert.False(options.UsesOutbox("ApplicationSubmittedEvent", null));
    }

    [Fact]
    public void UsesOutbox_IsFalse_WhenDisabled_EvenInAllMode()
    {
        var options = new OutboxOptions { Enabled = false, Mode = OutboxRoutingMode.All };

        Assert.False(options.UsesOutbox("ScanRequestedEvent"));
    }

    [Fact]
    public void BindsFromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MassTransit:Outbox:Mode"] = "Allowlist",
                ["MassTransit:Outbox:Events:0"] = "ApplicationResponseSaved",
                ["MassTransit:Outbox:Events:1"] = "ScanRequestedEvent"
            })
            .Build();

        var options = config.GetSection(OutboxOptions.SectionName).Get<OutboxOptions>()!;

        Assert.True(options.Enabled);
        Assert.Equal(["ApplicationResponseSaved", "ScanRequestedEvent"], options.Events);
    }
}
