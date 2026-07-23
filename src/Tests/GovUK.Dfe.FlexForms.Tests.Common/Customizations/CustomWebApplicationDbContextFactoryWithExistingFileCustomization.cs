using AutoFixture;
using GovUK.Dfe.CoreLibs.Testing.Mocks.Authentication;
using GovUK.Dfe.CoreLibs.Testing.Mocks.WebApplicationFactory;
using GovUK.Dfe.FlexForms.Api;
using GovUK.Dfe.FlexForms.Api.Client.Extensions;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Tests.Common.Seeders;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Notifications.Interfaces;
using GovUK.Dfe.FlexForms.Api.Middleware;
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
using GovUK.Dfe.FlexForms.Api.Client;
using GovUK.Dfe.FlexForms.Api.Client.Contracts;
using GovUK.Dfe.CoreLibs.Utilities.RateLimiting;

namespace GovUK.Dfe.FlexForms.Tests.Common.Customizations
{
    /// <summary>
    /// Same as <see cref="CustomWebApplicationDbContextFactoryCustomization"/> but seeds an existing File
    /// and a latest ApplicationResponse that references it, for tests that expect 409 when uploading a file that already exists.
    /// </summary>
    public class CustomWebApplicationDbContextFactoryWithExistingFileCustomization : ICustomization
    {
        private const string TestTenantId = "11111111-1111-4111-8111-111111111111";

        public void Customize(IFixture fixture)
        {
            fixture.Customize<CustomWebApplicationDbContextFactory<Program>>(composer => composer.FromFactory(() =>
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Local");
                Environment.SetEnvironmentVariable("ConnectionStrings__Redis", "localhost:6379");
                Environment.SetEnvironmentVariable("NotificationService__RedisConnectionString", "localhost:6379");
                Environment.SetEnvironmentVariable("DataProtection__UseAzure", "false");
                Environment.SetEnvironmentVariable("SkipMassTransit", "false");
                Environment.SetEnvironmentVariable("MassTransit__Transport", "AzureServiceBus");
                Environment.SetEnvironmentVariable("MassTransit__AppPrefix", "");
                Environment.SetEnvironmentVariable("MassTransit__AzureServiceBus__ConnectionString", "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test=");
                Environment.SetEnvironmentVariable("MassTransit__AzureServiceBus__AutoCreateEntities", "false");
                Environment.SetEnvironmentVariable("MassTransit__AzureServiceBus__ConfigureEndpoints", "false");
                Environment.SetEnvironmentVariable("MassTransit__AzureServiceBus__UseWebSockets", "true");
                Environment.SetEnvironmentVariable("Email__ServiceSupportEmailAddress", "some.email@education.gov.uk");

                var tokenConfig = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        { "Authorization:TokenSettings:SecretKey", "iw5/ivfUWaCpj+n3TihlGUzRVna+KKu8IfLP52GdgNXlDcqt3+N2MM45rwQ=" },
                        { "Authorization:TokenSettings:Issuer", "21f3ed37-8443-4755-9ed2-c68ca86b4398" },
                        { "Authorization:TokenSettings:Audience", "20dafd6d-79e5-4caf-8b72-d070dcc9716f" },
                        { "Authorization:TokenSettings:TokenLifetimeMinutes", "60" },
                        { "NotificationService:StorageProvider", "Redis" },
                        { "NotificationService:MaxNotificationsPerUser", "50" },
                        { "NotificationService:AutoCleanupIntervalMinutes", "60" },
                        { "NotificationService:MaxNotificationAgeHours", "24" },
                        { "NotificationService:RedisConnectionString", "localhost:6379" },
                        { "NotificationService:RedisKeyPrefix", "notifications:" },
                        { "NotificationService:SessionKey", "UserNotifications" }
                    })
                    .Build();

                var factory = new CustomWebApplicationDbContextFactory<Program>()
                {
                    SeedData = new Dictionary<Type, Action<DbContext>>
                    {
                        { typeof(ExternalApplicationsContext), context => EaContextSeeder.SeedTestDataWithExistingFile((ExternalApplicationsContext)context) },
                    },
                    ExternalServicesConfiguration = services =>
                    {
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
                            .AddScheme<AuthenticationSchemeOptions, MockJwtBearerHandler>(AuthConstants.TenantBearer, _ => { })
                            .AddScheme<AuthenticationSchemeOptions, MockCookieAuthenticationHandler>("HubCookie", _ => { });

                        services.RemoveAll<IExternalIdentityValidator>();
                        services.RemoveAll<IUserTokenService>();
                        services.RemoveAll<IRateLimiterFactory<string>>();
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
                        services.AddSingleton<IRateLimiterFactory<string>, MockRateLimiterFactory>();
                        services.RemoveAll<INotificationService>();
                        services.AddSingleton<INotificationService, MockNotificationService>();
                        services.RemoveAll<GovUK.Dfe.CoreLibs.Email.Interfaces.IEmailService>();
                        services.AddSingleton<GovUK.Dfe.CoreLibs.Email.Interfaces.IEmailService, MockEmailService>();
                        services.RemoveAll<GovUK.Dfe.CoreLibs.FileStorage.Interfaces.IFileStorageService>();
                        services.AddSingleton<GovUK.Dfe.CoreLibs.FileStorage.Interfaces.IFileStorageService, MockFileStorageService>();
                        services.RemoveAll<GovUK.Dfe.FlexForms.Application.Services.ITenantAwareFileStorageService>();
                        services.AddSingleton<GovUK.Dfe.FlexForms.Application.Services.ITenantAwareFileStorageService, MockFileStorageService>();
                        services.RemoveAll<GovUK.Dfe.CoreLibs.FileStorage.Interfaces.IAzureSpecificOperations>();
                        services.AddSingleton<GovUK.Dfe.CoreLibs.FileStorage.Interfaces.IAzureSpecificOperations, MockAzureSpecificOperations>();
                        services.RemoveAll<GovUK.Dfe.CoreLibs.Messaging.MassTransit.Interfaces.IEventPublisher>();
                        services.AddSingleton<GovUK.Dfe.CoreLibs.Messaging.MassTransit.Interfaces.IEventPublisher, MockEventPublisher>();
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
