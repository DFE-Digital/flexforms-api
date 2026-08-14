using GovUK.Dfe.FlexForms.Application.Messaging;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Events;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.Common.Pipeline;

/// <summary>
/// MassTransit consume filter that resolves the tenant from the inbound message's headers
/// (set by <c>TenantAwareEventPublisher</c>) and populates <see cref="ITenantContextAccessor.CurrentTenant"/>
/// before the consumer body runs. Allows a single shared subscription to serve all tenants.
/// Scan results also fall back to payload metadata when the scanner omitted headers.
/// </summary>
public sealed class TenantContextConsumeFilter<T>(
    ITenantContextAccessor tenantContextAccessor,
    ITenantConfigurationProvider tenantConfigurationProvider,
    ILogger<TenantContextConsumeFilter<T>> logger) : IFilter<ConsumeContext<T>>
    where T : class
{
    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        var metadata = context.Message is ScanResultEvent scanResult ? scanResult.Metadata : null;
        var tenantIdValue = ScanEventRouting.ResolveTenantId(context.Headers, metadata);

        if (Guid.TryParse(tenantIdValue, out var tenantId))
        {
            var tenant = tenantConfigurationProvider.GetTenant(tenantId);
            if (tenant is not null)
            {
                tenantContextAccessor.CurrentTenant = tenant;
                logger.LogDebug(
                    "Resolved tenant from message: {TenantId} ({TenantName}) for {MessageType}",
                    tenantId, tenant.Name, typeof(T).Name);

                await next.Send(context);
                return;
            }

            logger.LogWarning(
                "Message of type {MessageType} has TenantId '{TenantId}' but no matching tenant configuration was found; skipping",
                typeof(T).Name, tenantId);
            return;
        }

        logger.LogWarning(
            "Message of type {MessageType} has no '{Header}' header or metadata; skipping so the first-tenant DB is not used",
            typeof(T).Name, ScanEventRouting.TenantIdHeader);
    }

    public void Probe(ProbeContext context)
        => context.CreateFilterScope("tenantContext");
}
