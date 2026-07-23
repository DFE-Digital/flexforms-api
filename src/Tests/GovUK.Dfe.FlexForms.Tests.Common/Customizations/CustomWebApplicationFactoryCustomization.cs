using AutoFixture;
using GovUK.Dfe.CoreLibs.Testing.Mocks.Authentication;
using GovUK.Dfe.CoreLibs.Testing.Mocks.WebApplicationFactory;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Tests.Common.Customizations
{
    public class CustomWebApplicationFactoryCustomization<TProgram> : ICustomization
        where TProgram : class {

        public void Customize(IFixture fixture)
        {
            // Shared host-level Redis for CoreLibs registration before ExternalServicesConfiguration runs.
            Environment.SetEnvironmentVariable("ConnectionStrings__Redis", "localhost:6379");
            Environment.SetEnvironmentVariable("NotificationService__RedisConnectionString", "localhost:6379");
            // Development WebApplicationFactory must not attempt Azure Data Protection.
            Environment.SetEnvironmentVariable("DataProtection__UseAzure", "false");

            fixture.Customize<CustomWebApplicationFactory<TProgram>>(composer => composer.FromFactory(() =>
            {

                var factory = new CustomWebApplicationFactory<TProgram>()
                {
                    ExternalServicesConfiguration = services =>
                    {
                        services.PostConfigure<AuthenticationOptions>(options =>
                        {
                            options.DefaultAuthenticateScheme = "TestScheme";
                            options.DefaultChallengeScheme = "TestScheme";
                        });

                        services.AddAuthentication("TestScheme")
                            .AddScheme<AuthenticationSchemeOptions, MockJwtBearerHandler>("TestScheme", options => { });
                    },
                    ExternalHttpClientConfiguration = client =>
                    {
                        client.DefaultRequestHeaders.Authorization =
                            new AuthenticationHeaderValue("Bearer", "external-mock-token");
                    }
                };

                var client = factory.CreateClient();

                var config = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        { "ApiClient:BaseUrl", client.BaseAddress!.ToString() }
                    })
                    .Build();


                var services = new ServiceCollection();
                services.AddSingleton<IConfiguration>(config);

                var serviceProvider = services.BuildServiceProvider();

                fixture.Inject(factory);
                fixture.Inject(serviceProvider);
                fixture.Inject(client);
                fixture.Inject(new List<Claim>());

                return factory;
            }));

        }
    }
}
