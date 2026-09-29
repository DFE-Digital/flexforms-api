using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace GovUK.Dfe.FlexForms.Infrastructure.Messaging.Outbox;

/// <summary>
/// Wraps the root provider so every scope it creates already has <see cref="ITenantContextAccessor.CurrentTenant"/>
/// set. MassTransit's outbox delivery service resolves the DbContext from a fresh scope, and the
/// EA DbContext picks its connection string from the current tenant.
/// </summary>
public sealed class TenantScopedServiceProvider(IServiceProvider root, TenantConfiguration tenant)
    : IServiceProvider, IServiceScopeFactory
{
    public object? GetService(Type serviceType)
        => serviceType == typeof(IServiceScopeFactory) ? this : root.GetService(serviceType);

    public IServiceScope CreateScope()
    {
        var scope = root.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().CurrentTenant = tenant;
        return scope;
    }
}
