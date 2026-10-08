using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Utils.Configuration;
using Microsoft.Extensions.Configuration;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Resolves the outbox routing for a tenant: keys present in the tenant's <c>MassTransit:Outbox</c>
/// settings override the host <see cref="OutboxOptions"/>; missing keys inherit the host value.
/// </summary>
/// <remarks>
/// A tenant can only narrow or redirect routing. When the host has the outbox disabled it is not
/// registered at all, so a tenant <c>Enabled: true</c> cannot switch it on.
/// </remarks>
public static class TenantOutboxRouting
{
    public static OutboxOptions Resolve(OutboxOptions host, TenantConfiguration? tenant)
    {
        var section = tenant?.Settings.GetSection(OutboxOptions.SectionName);
        if (section is null || !section.Exists())
            return host;

        var resolved = new OutboxOptions
        {
            Enabled = host.Enabled,
            Mode = host.Mode,
            Events = host.Events
        };

        if (bool.TryParse(section[nameof(OutboxOptions.Enabled)], out var enabled))
            resolved.Enabled = host.Enabled && enabled;

        if (Enum.TryParse<OutboxRoutingMode>(section[nameof(OutboxOptions.Mode)], ignoreCase: true, out var mode))
            resolved.Mode = mode;

        var events = section.GetSection(nameof(OutboxOptions.Events));
        if (events.Exists())
        {
            IEnumerable<string?> values = events.Value is not null
                ? [events.Value]
                : events.GetChildren().Select(c => c.Value);

            resolved.Events = values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!.Trim())
                .ToArray();
        }

        return resolved;
    }
}
