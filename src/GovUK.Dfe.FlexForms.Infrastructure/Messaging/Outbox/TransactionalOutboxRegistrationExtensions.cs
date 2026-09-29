using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Infrastructure.Messaging.Outbox;
using GovUK.Dfe.FlexForms.Utils.Configuration;
using MassTransit;
using MassTransit.Middleware.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class TransactionalOutboxRegistrationExtensions
    {
        /// <summary>
        /// Registers the MassTransit EF Core bus outbox on <see cref="ExternalApplicationsContext"/>.
        /// Messages are stored in the tenant's EA database on <c>SaveChangesAsync</c> and delivered by
        /// <see cref="TenantOutboxDeliveryService"/>. Which events actually use it is decided per event
        /// by <see cref="OutboxOptions"/>; consumers are unaffected.
        /// </summary>
        public static void AddFlexFormsTransactionalOutbox(this IBusRegistrationConfigurator bus, IConfiguration config)
        {
            var section = config.GetSection(OutboxOptions.SectionName);
            if (!section.GetValue(nameof(OutboxOptions.Enabled), true))
                return;

            bus.AddEntityFrameworkOutbox<ExternalApplicationsContext>(o =>
            {
                o.UseSqlServer();
                // The inbox is only used by consumer-side outboxes, which are not enabled. Its cleanup
                // service is also not tenant-aware.
                o.DisableInboxCleanupService();
                o.UseBusOutbox(b => b.DisableDeliveryService());
            });

            bus.AddOptions<OutboxDeliveryServiceOptions>().Bind(section.GetSection("Delivery"));

            bus.AddSingleton<TenantOutboxNotificationHub>();
            bus.Replace(ServiceDescriptor.Singleton<IBusOutboxNotification>(
                sp => sp.GetRequiredService<TenantOutboxNotificationHub>()));

            bus.AddHostedService<TenantOutboxDeliveryService>();
        }
    }
}
