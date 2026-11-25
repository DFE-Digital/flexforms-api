using GovUK.Dfe.CoreLibs.Caching.Interfaces;
using GovUK.Dfe.CoreLibs.Notifications.Interfaces;
using GovUK.Dfe.CoreLibs.Utilities.RateLimiting;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace GovUK.Dfe.FlexForms.Tests.Common.Helpers;

/// <summary>
/// The parts of the integration test host that have nothing to do with authentication: process
/// environment for Program.cs, and in-memory stand-ins for Redis, email, notifications, file storage,
/// the bus and the HTML generator. Shared by the mock-auth and the real-auth (two-tenant) hosts.
/// </summary>
public static class IntegrationTestServices
{
    public static void ApplyHostEnvironment()
    {
        // Set environment to "Local" to bypass Azure-specific operations in tests
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Local");
        IntegrationTestHostBootstrap.Apply();

        // Host Redis for CoreLibs DI (tenant DB settings may omit it; overlay from root).
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", "localhost:6379");
        Environment.SetEnvironmentVariable("NotificationService__RedisConnectionString", "localhost:6379");
        Environment.SetEnvironmentVariable("DataProtection__UseAzure", "false");

        // Set environment variables for MassTransit configuration
        // These will be picked up by ConfigurationBuilder in Program.cs
        // For tests, provide a dummy connection string to satisfy validation
        Environment.SetEnvironmentVariable("SkipMassTransit", "true");
        Environment.SetEnvironmentVariable("MassTransit__Transport", "AzureServiceBus");
        Environment.SetEnvironmentVariable("MassTransit__AppPrefix", "");
        // Dummy connection string for tests - format: Endpoint=sb://...;SharedAccessKeyName=...;SharedAccessKey=...
        Environment.SetEnvironmentVariable("MassTransit__AzureServiceBus__ConnectionString", "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test=");
        Environment.SetEnvironmentVariable("MassTransit__AzureServiceBus__AutoCreateEntities", "false");
        Environment.SetEnvironmentVariable("MassTransit__AzureServiceBus__ConfigureEndpoints", "false");
        Environment.SetEnvironmentVariable("MassTransit__AzureServiceBus__UseWebSockets", "true");
        // Configure service support email address for testing user feedback/support
        Environment.SetEnvironmentVariable("Email__ServiceSupportEmailAddress", "some.email@education.gov.uk");
    }

    public static void ReplaceExternalServices(IServiceCollection services, string tenantConfigDatabaseName)
    {
        services.RemoveAll<IRateLimiterFactory<string>>();
        services.AddSingleton<IRateLimiterFactory<string>, MockRateLimiterFactory>();

        // Replace the notification service with our mock
        services.RemoveAll<INotificationService>();
        services.AddSingleton<INotificationService, MockNotificationService>();

        // Replace the email service with our mock to avoid sending actual emails in tests
        services.RemoveAll<GovUK.Dfe.CoreLibs.Email.Interfaces.IEmailService>();
        services.AddSingleton<GovUK.Dfe.CoreLibs.Email.Interfaces.IEmailService, MockEmailService>();

        // Replace the file storage service with our mock to avoid requiring actual Azure Storage connection strings in tests
        services.RemoveAll<GovUK.Dfe.CoreLibs.FileStorage.Interfaces.IFileStorageService>();
        services.AddSingleton<GovUK.Dfe.CoreLibs.FileStorage.Interfaces.IFileStorageService, MockFileStorageService>();

        // Also register our mock for the tenant-aware interface used by handlers
        services.RemoveAll<GovUK.Dfe.FlexForms.Application.Services.ITenantAwareFileStorageService>();
        services.AddSingleton<GovUK.Dfe.FlexForms.Application.Services.ITenantAwareFileStorageService, MockFileStorageService>();

        // Replace IAzureSpecificOperations with our mock to avoid requiring actual Azure Storage for SAS token generation
        services.RemoveAll<GovUK.Dfe.CoreLibs.FileStorage.Interfaces.IAzureSpecificOperations>();
        services.AddSingleton<GovUK.Dfe.CoreLibs.FileStorage.Interfaces.IAzureSpecificOperations, MockAzureSpecificOperations>();

        // Replace IEventPublisher with our mock to avoid hanging on MassTransit publish in tests
        services.RemoveAll<GovUK.Dfe.CoreLibs.Messaging.MassTransit.Interfaces.IEventPublisher>();
        services.AddSingleton<GovUK.Dfe.CoreLibs.Messaging.MassTransit.Interfaces.IEventPublisher, MockEventPublisher>();

        // Replace IStaticHtmlGeneratorService with our mock to avoid requiring Playwright in tests
        services.RemoveAll<GovUK.Dfe.FlexForms.Domain.Services.IStaticHtmlGeneratorService>();
        services.AddSingleton<GovUK.Dfe.FlexForms.Domain.Services.IStaticHtmlGeneratorService, MockStaticHtmlGeneratorService>();

        // Replace Redis with in-memory alternatives so tests don't require a running Redis server
        services.RemoveAll<IConnectionMultiplexer>();
        services.RemoveAll<IDistributedCache>();
        services.RemoveAll<ICacheService<IRedisCacheType>>();
        services.RemoveAll<IAdvancedRedisCacheService>();
        services.AddDistributedMemoryCache();
        var mockRedisCache = new MockRedisCacheService();
        services.AddSingleton<ICacheService<IRedisCacheType>>(mockRedisCache);
        services.AddSingleton<IAdvancedRedisCacheService>(mockRedisCache);
        services.RemoveAll<GovUK.Dfe.FlexForms.Domain.Caching.IAtomicCacheStore>();
        services.AddSingleton<GovUK.Dfe.FlexForms.Domain.Caching.IAtomicCacheStore, InMemoryAtomicCacheStore>();

        // The base factory strips every DbContextOptions registration; only the EA context is re-added
        // through SeedData, so TenantConfig consumers (e.g. Test Authentication authenticators) need this.
        services.AddDbContext<TenantConfigDbContext>(options => options.UseInMemoryDatabase(tenantConfigDatabaseName));
    }
}
