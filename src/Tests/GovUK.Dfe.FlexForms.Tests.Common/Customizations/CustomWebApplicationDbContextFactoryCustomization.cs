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
using GovUK.Dfe.FlexForms.Domain.Tenancy;
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
    public class CustomWebApplicationDbContextFactoryCustomization : ICustomization
    {
        /// <summary>
        /// Tenant ID used for integration test requests. Must match a tenant in appsettings (e.g. Tenants:Transfers:Id).
        /// Required so TenantResolutionMiddleware does not return 400 "Missing or invalid tenant id header".
        /// </summary>
        private const string TestTenantId = "11111111-1111-4111-8111-111111111111";

        public void Customize(IFixture fixture)
        {
            fixture.Customize<CustomWebApplicationDbContextFactory<Program>>(composer => composer.FromFactory(() =>
            {
                IntegrationTestServices.ApplyHostEnvironment();
                
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
                        { "NotificationService:RedisKeyPrefix", "FlexForms:notifications:" },
                        { "NotificationService:SessionKey", "UserNotifications" }
                    })
                    .Build();

                var tenantConfigDatabaseName = $"tenantconfig-{Guid.NewGuid()}";
                var factory = new CustomWebApplicationDbContextFactory<Program>()
                {
                    SeedData = new Dictionary<Type, Action<DbContext>>
                    {
                        { typeof(ExternalApplicationsContext), context => EaContextSeeder.SeedTestData((ExternalApplicationsContext)context) },
                    },
                    ExternalServicesConfiguration = services =>
                    {
                        // Use tenant config from customization (FileStorage:Local with AllowedExtensions) so tests don't depend on appsettings
                        services.RemoveAll<ITenantConfigurationProvider>();
                        services.AddSingleton<ITenantConfigurationProvider>(new TestTenantConfigurationProvider(TestTenantId));
                        services.RemoveAll<ITenantConfigurationRefreshState>();
                        services.AddSingleton<ITenantConfigurationRefreshState>(new TestTenantConfigurationRefreshState());

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
                                // SaaS test dispatcher: all bearer traffic forwards to TenantBearer.
                                schemeOptions.ForwardDefaultSelector = _ => AuthConstants.TenantBearer;
                            })
                            .AddScheme<AuthenticationSchemeOptions, MockJwtBearerHandler>(
                                AuthConstants.TenantBearer,
                                _ => { /* picks up factory.TestClaims */ })

                            // Add HubCookie scheme for SignalR authentication with proper cookie handler
                            .AddScheme<AuthenticationSchemeOptions, MockCookieAuthenticationHandler>(
                                "HubCookie",
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
                        IntegrationTestServices.ReplaceExternalServices(services, tenantConfigDatabaseName);
                    },
                    ExternalHttpClientConfiguration = client =>
                    {
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "external-mock-token");
                        // Multitenancy: API returns 400 when X-Tenant-ID is missing (TenantResolutionMiddleware)
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
                services.AddExternalApplicationsApiClient<IRolesClient, RolesClient>(config, client);
                services.AddExternalApplicationsApiClient<ITenantAdminClient, TenantAdminClient>(config, client);

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
                fixture.Inject(serviceProvider.GetRequiredService<IRolesClient>());
                fixture.Inject(serviceProvider.GetRequiredService<ITenantAdminClient>());
                fixture.Inject(serviceProvider.GetRequiredService<IExternalIdentityValidator>());
                fixture.Inject(new List<Claim>());

                return factory;
            }));
        }
    }
}
