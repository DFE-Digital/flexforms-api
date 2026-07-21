using AutoFixture;
using GovUK.Dfe.CoreLibs.Testing.Mocks.Authentication;
using GovUK.Dfe.CoreLibs.Testing.Mocks.WebApplicationFactory;
using GovUK.Dfe.FlexForms.Api;
using GovUK.Dfe.FlexForms.Api.Middleware;
using GovUK.Dfe.FlexForms.Api.Client.Extensions;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Tests.Common.Seeders;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Security.Claims;
using GovUK.Dfe.FlexForms.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using GovUK.Dfe.CoreLibs.Security.Interfaces;
using GovUK.Dfe.CoreLibs.Security;
using GovUK.Dfe.CoreLibs.Caching.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;
using GovUK.Dfe.FlexForms.Tests.Common.Helpers;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Api.Client;
using GovUK.Dfe.FlexForms.Api.Client.Contracts;
using GovUK.Dfe.CoreLibs.Notifications.Interfaces;

namespace GovUK.Dfe.FlexForms.Tests.Common.Customizations
{
    /// <summary>
    /// Test customization that keeps the real rate limiter for testing rate limiting behavior.
    /// Sends X-Tenant-ID so requests pass TenantResolutionMiddleware and reach the handler (otherwise 400).
    /// </summary>
    public class CustomWebApplicationDbContextFactoryWithRateLimitingCustomization : ICustomization
    {
        private const string TestTenantId = "11111111-1111-4111-8111-111111111111";

        public void Customize(IFixture fixture)
        {
            fixture.Customize<CustomWebApplicationDbContextFactory<Program>>(composer => composer.FromFactory(() =>
            {
                // Set environment to "Local" to bypass Azure-specific operations in tests
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Local");
                
                // Set environment variables for MassTransit configuration
                // These will be picked up by ConfigurationBuilder in Program.cs
                // For tests, provide a dummy connection string to satisfy validation
                Environment.SetEnvironmentVariable("SkipMassTransit", "false");
                Environment.SetEnvironmentVariable("MassTransit__Transport", "AzureServiceBus");
                Environment.SetEnvironmentVariable("MassTransit__AppPrefix", "");
                // Dummy connection string for tests - format: Endpoint=sb://...;SharedAccessKeyName=...;SharedAccessKey=...
                Environment.SetEnvironmentVariable("MassTransit__AzureServiceBus__ConnectionString", "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test=");
                Environment.SetEnvironmentVariable("MassTransit__AzureServiceBus__AutoCreateEntities", "false");
                Environment.SetEnvironmentVariable("MassTransit__AzureServiceBus__ConfigureEndpoints", "false");
                Environment.SetEnvironmentVariable("MassTransit__AzureServiceBus__UseWebSockets", "true");
                // Configure service support email address for testing user feedback/support
                Environment.SetEnvironmentVariable("Email__ServiceSupportEmailAddress", "some.email@education.gov.uk");
                
                var tokenConfig = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        { "Authorization:TokenSettings:SecretKey", "iw5/ivfUWaCpj+n3TihlGUzRVna+KKu8IfLP52GdgNXlDcqt3+N2MM45rwQ=" },
                        { "Authorization:TokenSettings:Issuer", "21f3ed37-8443-4755-9ed2-c68ca86b4398" },
                        { "Authorization:TokenSettings:Audience", "20dafd6d-79e5-4caf-8b72-d070dcc9716f" },
                        { "Authorization:TokenSettings:TokenLifetimeMinutes", "60" }
                    })
                    .Build();

                var factory = new CustomWebApplicationDbContextFactory<Program>()
                {
                    SeedData = new Dictionary<Type, Action<DbContext>>
                    {
                        { typeof(ExternalApplicationsContext), context => EaContextSeeder.SeedTestData((ExternalApplicationsContext)context) },
                    },
                    ExternalServicesConfiguration = services =>
                    {
                        // Use tenant config from customization (HostMappings) so template membership checks match seeded data.
                        services.RemoveAll<ITenantConfigurationProvider>();
                        services.AddSingleton<ITenantConfigurationProvider>(new TestTenantConfigurationProvider(TestTenantId));

                        services.RemoveAll(typeof(IConfigureOptions<AuthenticationOptions>));
                        services.RemoveAll(typeof(IConfigureOptions<JwtBearerOptions>));
                        services.RemoveAll<IPostConfigureOptions<AuthenticationOptions>>();
                        services.RemoveAll<IPostConfigureOptions<JwtBearerOptions>>();

                        services.AddAuthentication(options =>
                            {
                                options.DefaultAuthenticateScheme = "CompositeScheme";
                                options.DefaultChallengeScheme = "CompositeScheme";
                            })
                            .AddPolicyScheme("CompositeScheme", "CompositeAuth", schemeOptions =>
                            {
                                schemeOptions.ForwardDefaultSelector = _ => AuthConstants.TenantBearer;
                            })
                            .AddScheme<AuthenticationSchemeOptions, MockJwtBearerHandler>(
                                AuthConstants.TenantBearer,
                                _ => { /* picks up factory.TestClaims */ });

                        services.RemoveAll<IExternalIdentityValidator>();
                        services.RemoveAll<IUserTokenService>();

                        services.AddTransient<IExternalIdentityValidator, TestExternalIdentityValidator>();
                        services.AddUserTokenService(tokenConfig);

                        // SaaS: register named TokenSettings for the test tenant so
                        // IUserTokenServiceFactory.GetService(TestTenantId) returns a service
                        // signing with the test key/issuer/audience.
                        services.Configure<GovUK.Dfe.CoreLibs.Security.Configurations.TokenSettings>(TestTenantId, opts =>
                        {
                            opts.SecretKey = tokenConfig["Authorization:TokenSettings:SecretKey"]!;
                            opts.Issuer = tokenConfig["Authorization:TokenSettings:Issuer"]!;
                            opts.Audience = tokenConfig["Authorization:TokenSettings:Audience"]!;
                            opts.TokenLifetimeMinutes = int.Parse(tokenConfig["Authorization:TokenSettings:TokenLifetimeMinutes"] ?? "60");
                        });
                        
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
                        
                        // Replace Redis with in-memory alternatives so tests don't require a running Redis server
                        services.RemoveAll<IConnectionMultiplexer>();
                        services.RemoveAll<IDistributedCache>();
                        services.RemoveAll<ICacheService<IRedisCacheType>>();
                        services.RemoveAll<IAdvancedRedisCacheService>();
                        services.AddDistributedMemoryCache();
                        var mockRedisCache = new MockRedisCacheService();
                        services.AddSingleton<ICacheService<IRedisCacheType>>(mockRedisCache);
                        services.AddSingleton<IAdvancedRedisCacheService>(mockRedisCache);
                    },
                    ExternalHttpClientConfiguration = client =>
                    {
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "external-mock-token");
                        client.DefaultRequestHeaders.Add(TenantResolutionMiddleware.TenantIdHeader, TestTenantId);
                    }
                };

                var client = factory.CreateClient();

                var config = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        { "ExternalApplicationsApiClient:BaseUrl", client.BaseAddress!.ToString() },
                        { "ExternalApplicationsApiClient:RequestTokenExchange", "false" },
                        { "ExternalApplicationsApiClient:TenantId", TestTenantId }
                    })
                    .Build();

                var services = new ServiceCollection();
                services.AddSingleton<IConfiguration>(config);
                services.AddExternalApplicationsApiClient<IUsersClient, UsersClient>(config, client);
                services.AddExternalApplicationsApiClient<ITemplatesClient, TemplatesClient>(config, client);
                services.AddExternalApplicationsApiClient<ITokensClient, TokensClient>(config, client);
                services.AddExternalApplicationsApiClient<IApplicationsClient, ApplicationsClient>(config, client);
                services.AddExternalApplicationsApiClient<INotificationsClient, NotificationsClient>(config, client);
                services.AddExternalApplicationsApiClient<IUserFeedbackClient, UserFeedbackClient>(config, client);

                services.RemoveAll<IExternalIdentityValidator>();
                services.RemoveAll<IUserTokenService>();

                services.AddTransient<IExternalIdentityValidator, TestExternalIdentityValidator>();
                services.AddUserTokenService(config);

                var serviceProvider = services.BuildServiceProvider();

                fixture.Inject(factory);
                fixture.Inject(serviceProvider);
                fixture.Inject(client);
                fixture.Inject(serviceProvider.GetRequiredService<IUsersClient>());
                fixture.Inject(serviceProvider.GetRequiredService<ITemplatesClient>());
                fixture.Inject(serviceProvider.GetRequiredService<ITokensClient>());
                fixture.Inject(serviceProvider.GetRequiredService<IApplicationsClient>());
                fixture.Inject(serviceProvider.GetRequiredService<INotificationsClient>());
                fixture.Inject(serviceProvider.GetRequiredService<IUserFeedbackClient>());
                fixture.Inject(serviceProvider.GetRequiredService<IExternalIdentityValidator>());
                fixture.Inject(new List<Claim>());

                return factory;
            }));
        }
    }
}
