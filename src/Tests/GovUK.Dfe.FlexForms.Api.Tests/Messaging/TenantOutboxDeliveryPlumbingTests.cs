using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Infrastructure.Messaging.Outbox;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Messaging;

public class TenantOutboxDeliveryPlumbingTests
{
    [Fact]
    public void TenantScopedServiceProvider_SetsTenantOnEveryScope()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContextAccessor, TestTenantContextAccessor>();
        using var root = services.BuildServiceProvider();

        var tenant = new TenantConfiguration(Guid.NewGuid(), "Transfers", new ConfigurationBuilder().Build(), []);
        IServiceProvider provider = new TenantScopedServiceProvider(root, tenant);

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Assert.Same(tenant, first.ServiceProvider.GetRequiredService<ITenantContextAccessor>().CurrentTenant);
        Assert.Same(tenant, second.ServiceProvider.GetRequiredService<ITenantContextAccessor>().CurrentTenant);
    }

    [Fact]
    public async Task NotificationHub_Delivered_WakesEveryTenantLoop()
    {
        var hub = new TenantOutboxNotificationHub(
            Options.Create(new OutboxDeliveryServiceOptions { QueryDelay = TimeSpan.FromMinutes(5) }));

        var tenantA = hub.GetOrAdd("db-a");
        var tenantB = hub.GetOrAdd("db-b");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var waitA = tenantA.WaitForDelivery(timeout.Token);
        var waitB = tenantB.WaitForDelivery(timeout.Token);

        hub.Delivered();

        await Task.WhenAll(waitA, waitB).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void NotificationHub_ReusesNotification_ForTheSameDatabase()
    {
        var hub = new TenantOutboxNotificationHub(Options.Create(new OutboxDeliveryServiceOptions()));

        Assert.Same(hub.GetOrAdd("Server=a;Database=EA"), hub.GetOrAdd("server=A;database=ea"));
    }

    private sealed class TestTenantContextAccessor : ITenantContextAccessor
    {
        public TenantConfiguration? CurrentTenant { get; set; }
    }
}
