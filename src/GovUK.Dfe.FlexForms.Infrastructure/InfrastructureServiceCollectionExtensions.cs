using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Infrastructure;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Infrastructure.Repositories;
using GovUK.Dfe.FlexForms.Infrastructure.Services;
using GovUK.Dfe.FlexForms.Utils.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class InfrastructureServiceCollectionExtensions
    {
        public static IServiceCollection AddInfrastructureDependencyGroup(
            this IServiceCollection services, 
            IConfiguration config,
            ITenantConfigurationProvider tenantConfigurationProvider)
        {
            // Get the first tenant's configuration for services that need root-level config
            var firstTenant = tenantConfigurationProvider.GetAllTenants().FirstOrDefault()
                ?? throw new InvalidOperationException("At least one tenant must be configured.");
            var tenantConfig = CoreLibsHostConfiguration.Resolve(config, firstTenant.Settings);
            
            // Store the first tenant's connection string for fallback in message consumers
            // Message consumers may not have HTTP context to resolve tenant
            var fallbackConnectionString = firstTenant.GetConnectionString("DefaultConnection")
                ?? config.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "First tenant must have a DefaultConnection connection string (or set ConnectionStrings:DefaultConnection on the host).");

            //Repos
            services.AddScoped(typeof(IEaRepository<>), typeof(EaRepository<>));
            services.AddScoped<IApplicationRepository, ApplicationRepository>();
            services.AddScoped<IUnitOfWork, UnitOfWork>();

            //Cache service - use tenant config (Redis for everything + IDistributedCache)
            services.AddHybridCaching(tenantConfig);

            services.AddTransient<IApplicationReferenceProvider, DefaultApplicationReferenceProvider>();
            services.AddTransient<IApplicationResponseAppender, ApplicationResponseAppender>();
            
            // Static HTML Generator Service
            services.AddScoped<IStaticHtmlGeneratorService, PlaywrightHtmlGeneratorService>();

            // SignalR Services
            services.AddScoped<INotificationSignalRService, NotificationSignalRService>();

            //Db - with fallback for message consumers that don't have tenant context yet
            services.AddDbContext<ExternalApplicationsContext>((serviceProvider, options) =>
            {
                var tenantAccessor = serviceProvider.GetRequiredService<ITenantContextAccessor>();
                var tenantProvider = serviceProvider.GetRequiredService<ITenantConfigurationProvider>();
                
                string connectionString;
                
                if (tenantAccessor.CurrentTenant != null)
                {
                    // Normal case: tenant context is available (HTTP request)
                    var tenant = tenantAccessor.CurrentTenant;
                    connectionString = tenant.GetConnectionString("DefaultConnection") 
                        ?? throw new InvalidOperationException($"Tenant '{tenant.Name}' is missing DefaultConnection connection string.");
                }
                else
                {
                    // Fallback case: no tenant context (message consumer)
                    // Use first tenant's connection string and log a warning
                    // The consumer should set tenant context from message headers before doing tenant-specific operations
                    var logger = serviceProvider.GetService<ILogger<ExternalApplicationsContext>>();
                    logger?.LogDebug(
                        "No tenant context available during DbContext creation. Using fallback connection (first tenant). " +
                        "This is expected for message consumers - tenant context will be set from message headers.");
                    
                    // Try to get first tenant's connection string from provider (in case it changed)
                    var defaultTenant = tenantProvider.GetAllTenants().FirstOrDefault();
                    connectionString = defaultTenant?.GetConnectionString("DefaultConnection") ?? fallbackConnectionString;
                }

                options.UseSqlServer(connectionString, sql =>
                {
                });
            });

            return services;
        }
    }
}
