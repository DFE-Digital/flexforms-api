using GovUK.Dfe.FlexForms.Utils.Caching;
using GovUK.Dfe.FlexForms.Utils.Configuration;
using GovUK.Dfe.FlexForms.Application.Common.Behaviours;
using GovUK.Dfe.FlexForms.Application.Common.Pipeline;
using GovUK.Dfe.FlexForms.Application.Consumers;
using GovUK.Dfe.FlexForms.Application.Messaging;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.TenantAdmin.Validation;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Services.RoleProvisioners;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using System.Reflection;
using GovUK.Dfe.CoreLibs.FileStorage;
using GovUK.Dfe.CoreLibs.Notifications.Extensions;
using GovUK.Dfe.CoreLibs.Notifications.Interfaces;
using GovUK.Dfe.CoreLibs.Notifications.Services;
using GovUK.Dfe.FlexForms.Application.Notifications;
using GovUK.Dfe.CoreLibs.Utilities.RateLimiting;
using Microsoft.AspNetCore.Http;
using GovUK.Dfe.CoreLibs.Email;
using GovUK.Dfe.CoreLibs.Security.Interfaces;
using GovUK.Dfe.CoreLibs.Messaging.MassTransit.Extensions;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Entities.Topics;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Events;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Exceptions;
using GovUK.Dfe.CoreLibs.FileStorage.Interfaces;
using Microsoft.Extensions.Logging;
using MassTransit;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ApplicationServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationDependencyGroup(
            this IServiceCollection services, 
            IConfiguration config,
            ITenantConfigurationProvider tenantConfigurationProvider)
        {
            // CoreLibs FileStorage/Email/Notifications expect an IConfiguration shaped like their
            // root sections. Prefer GlobalConfiguration when those sections are present; otherwise
            // fall back to the first tenant for DI registration shape only. Runtime behaviour is
            // still tenant-aware via TenantAwareFileStorageService and related wrappers.
            var firstTenant = tenantConfigurationProvider.GetAllTenants().FirstOrDefault()
                ?? throw new InvalidOperationException("At least one tenant must be configured.");
            var tenantConfig = CoreLibsHostConfiguration.Resolve(config, firstTenant.Settings);
            
            // Performance logging is enabled if any tenant has it enabled
            var performanceLoggingEnabled = tenantConfigurationProvider
                .GetAllTenants()
                .Any(t => t.Settings.GetValue<bool>("Features:PerformanceLoggingEnabled"));

            services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly(), includeInternalTypes: true);

            services.AddRateLimiting<string>();
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
                cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(UnhandledExceptionBehaviour<,>));
                cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehaviour<,>));
                services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RateLimitingBehaviour<,>));

                if (performanceLoggingEnabled)
                {
                    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(PerformanceBehaviour<,>));
                }
            });
            services.AddScoped<IPermissionCheckerService, ClaimBasedPermissionCheckerService>();
            services.AddScoped<IUserCacheInvalidator, UserCacheInvalidator>();
            services.AddScoped<ITemplateSchemaCacheInvalidator, TemplateSchemaCacheInvalidator>();
            services.AddScoped<IAuthenticatedUserService, AuthenticatedUserService>();
            services.AddScoped<IApplicationCreationService, ApplicationCreationService>();
            services.AddScoped<ITenantRoleService, TenantRoleService>();
            services.AddScoped<ITenantMembershipService, TenantMembershipService>();
            services.AddScoped<IRolePermissionService, RolePermissionService>();
            services.AddSingleton<ITenantOidcAudienceBinder, TenantOidcAudienceBinder>();

            services.AddSingleton<IUserRoleProvisionerRegistry, UserRoleProvisionerRegistry>();
            services.AddTransient<IUserRoleProvisioner, StandardUserRoleProvisioner>();
            services.AddTransient<IUserRoleProvisioner, AdminRoleProvisioner>();

            services.AddKeyedScoped<ICustomRequestChecker, InternalAuthRequestChecker>("internal");

            services.AddTransient<IApplicationFactory, ApplicationFactory>();
            services.AddTransient<IUserFactory, UserFactory>();
            services.AddTransient<ITemplateFactory, TemplateFactory>();
            services.AddTransient<IFileFactory, FileFactory>();
            services.AddSingleton<IApplicationFileValidationPolicy, ApplicationFileValidationPolicy>();
            services.AddScoped<IFileValidationModeResolver, FileValidationModeResolver>();

            services.AddTransient<IEmailTemplateResolver, EmailTemplateResolver>();

            // Outbound mapped event publishing. Scoped so every lookup uses the request's tenant settings.
            services.AddScoped<IEventMappingProvider, EventMappingProvider>();
            services.AddScoped<ISchemaEventDefinitionProvider, SchemaEventDefinitionProvider>();
            services.AddScoped<IEventDataMapper, EventDataMapper>();
            services.AddSingleton<IEventTypeRegistry, EventTypeRegistry>();

            services.AddScoped<ITenantTemplateCatalogue, TenantTemplateCatalogue>();
            services.AddScoped<ITenantTemplateResolver, TenantTemplateResolver>();
            services.AddScoped<ITenantPermissionFilter, TenantPermissionFilter>();
            services.AddScoped<IUserAccessibleTemplateService, UserAccessibleTemplateService>();
            services.AddScoped<ISelfRegistrationTemplateAccessService, SelfRegistrationTemplateAccessService>();
            services.AddScoped<ITemplateHostMappingOwnershipValidator, TemplateHostMappingOwnershipValidator>();

            // Integration tests skip this hosted service: CoreLibs 1.0.13 throws
            // ChannelClosedException from BackgroundServiceFactory.StopAsync when the
            // WebApplicationFactory disposes (channel Complete is called twice).
            if (!config.GetValue<bool>("SkipBackgroundService", false))
            {
                services.AddBackgroundService();
            }
            
            // Host-shaped config for CoreLibs DI registration (see CoreLibsHostConfiguration.Resolve).
            // Resolve forces FlexForms Redis key prefixes (not DfE:Cache:) for shared Redis with EAT.
            services.AddNotificationServicesWithRedis(tenantConfig);
            services.PostConfigure<GovUK.Dfe.CoreLibs.Notifications.Options.NotificationServiceOptions>(options =>
            {
                options.RedisKeyPrefix = FlexFormsCacheKeys.NotificationsKeyPrefix;
            });
            services.AddScoped<INotificationService>(sp =>
            {
                var inner = ActivatorUtilities.CreateInstance<NotificationService>(sp);
                return new TenantScopedNotificationService(
                    inner,
                    sp.GetRequiredService<ITenantContextAccessor>());
            });

            services.AddFileStorage(tenantConfig);
            
            // Register the tenant-aware file storage wrapper
            // Register under a DIFFERENT interface to avoid breaking CoreLibs internal 
            services.AddScoped<ITenantAwareFileStorageService, TenantAwareFileStorageService>();

            services.AddEmailServicesWithGovUkNotify(tenantConfig);

            // Skip MassTransit during NSwag/CodeGeneration to prevent assembly loading issues
            var skipMassTransit = config.GetValue<bool>("SkipMassTransit", false);
            if (!skipMassTransit)
            {
                // SaaS model: ONE shared Azure Service Bus namespace for all tenants.
                // The bus host is driven by root host configuration (ConnectionStrings:ServiceBus
                // or MassTransit:AzureServiceBus:ConnectionString) - NOT per-tenant config.
                // Tenant context for inbound messages is set by TenantContextConsumeFilter<T>
                // from the 'TenantId' header on each message.
                //
                // Subscription naming convention: "{SubscriptionPrefix}-{topic-purpose}".
                // Prefix comes from config (e.g. "extapi", "extapi-staging") so it can be
                // varied per environment. The suffix is hardcoded next to each
                // SubscriptionEndpoint registration because it identifies what the
                // subscription is for - that's a code concern, not a config concern.
                //
                // To add a new topic in the future:
                //   1. Add the consumer to configureConsumers below
                //   2. Map the message type to its topic in configureBus below
                //   3. Add a new cfg.SubscriptionEndpoint<TEvent>($"{subscriptionPrefix}-foo", ...)
                //      block in configureAzureServiceBus below
                var subscriptionPrefix = config["MassTransit:SubscriptionPrefix"] ?? "extapi";

                services.AddDfEMassTransit(
                    config,
                    configureConsumers: x =>
                    {
                        x.AddConsumer<ScanResultConsumer>();
                    },
                    configureBus: (context, cfg) =>
                    {
                        // Any CoreLibs Messaging.Contracts event a tenant binds through
                        // EventTriggers needs its topic entity name registered.
                        MessagingEventBusConfigurator.ConfigureDiscoveredMessageTopics(cfg);

                        // Applied last so the scan pipeline never depends on name discovery.
                        cfg.Message<ScanRequestedEvent>(m => m.SetEntityName(TopicNames.ScanRequests));
                        cfg.Message<ScanResultEvent>(m => m.SetEntityName(TopicNames.ScanResult));
                    },
                    configureAzureServiceBus: (context, cfg) =>
                    {
                        cfg.UseJsonSerializer();

                        cfg.SubscriptionEndpoint<ScanResultEvent>($"{subscriptionPrefix}-scan-result", e =>
                        {
                            e.UseMessageRetry(r =>
                            {
                                r.Handle<MessageNotForThisInstanceException>();
                                r.Immediate(10);
                                r.Ignore<MessageNotForThisInstanceException>();
                                r.Interval(3, TimeSpan.FromSeconds(5));
                            });

                            e.UseConsumeFilter(typeof(TenantContextConsumeFilter<>), context);

                            e.ConfigureConsumeTopology = false;
                            e.ConfigureConsumer<ScanResultConsumer>(context);
                        });
                    }
                );

                // Register AFTER AddDfEMassTransit so this wins the IEventPublisher lookup
                // (AddDfEMassTransit registers MassTransitEventPublisher last, which would otherwise override).
                services.AddScoped<GovUK.Dfe.CoreLibs.Messaging.MassTransit.Interfaces.IEventPublisher, TenantAwareEventPublisher>();
                services.AddScoped<IEventTriggerDispatcher, EventTriggerDispatcher>();
            }
            else
            {
                // File upload/submit still resolve IEventTriggerDispatcher; MassTransit is not in the container.
                services.AddScoped<IEventTriggerDispatcher, NoOpEventTriggerDispatcher>();
            }

            return services;
        }
    }
}
