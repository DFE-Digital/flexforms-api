namespace GovUK.Dfe.FlexForms.Utils.Configuration;

public enum OutboxRoutingMode
{
    /// <summary>Only events listed in <see cref="OutboxOptions.Events"/> use the outbox.</summary>
    Allowlist,

    /// <summary>Every event published outside a consumer uses the outbox.</summary>
    All
}

/// <summary>
/// Controls which integration events go through the MassTransit transactional outbox.
/// Events that are not routed through the outbox are published straight to the bus (at most once),
/// which suits subscribers that cannot tolerate the occasional duplicate the outbox may produce.
/// </summary>
public sealed class OutboxOptions
{
    public const string SectionName = "MassTransit:Outbox";

    public bool Enabled { get; set; } = true;

    public OutboxRoutingMode Mode { get; set; } = OutboxRoutingMode.Allowlist;

    /// <summary>
    /// Event identifiers routed through the outbox in <see cref="OutboxRoutingMode.Allowlist"/> mode.
    /// Matched case-insensitively against the message type name, its full name, or (for schema
    /// events) the configured event type and topic name.
    /// </summary>
    public string[] Events { get; set; } = [];

    public bool UsesOutbox(params string?[] identifiers)
    {
        if (!Enabled)
            return false;

        if (Mode == OutboxRoutingMode.All)
            return true;

        return identifiers.Any(id =>
            !string.IsNullOrWhiteSpace(id)
            && Events.Contains(id, StringComparer.OrdinalIgnoreCase));
    }
}
