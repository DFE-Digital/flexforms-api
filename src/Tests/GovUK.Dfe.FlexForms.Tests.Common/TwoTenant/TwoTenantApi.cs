using System.Net.Http.Headers;
using System.Net.Http.Json;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Security.Interfaces;
using GovUK.Dfe.CoreLibs.Testing.Mocks.WebApplicationFactory;
using GovUK.Dfe.FlexForms.Api;
using GovUK.Dfe.FlexForms.Api.Middleware;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Infrastructure.Security;
using GovUK.Dfe.FlexForms.Tests.Common.Helpers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GovUK.Dfe.FlexForms.Tests.Common.TwoTenant;

/// <summary>
/// An API host with two tenants and the real authentication pipeline: <c>CompositeScheme</c>,
/// <c>TenantBearer</c> with the per-tenant token settings, <c>ApiKey</c>, claims transformation and the
/// permission claim provider. Nothing in it mocks a bearer token. User tokens come from the real
/// <c>POST /v1/tokens/exchange</c>, called the way Web calls it: the tenant's service API key plus a subject
/// token. Only the identity provider is a stand-in (<see cref="TestExternalIdentityValidator"/>).
/// Both tenants share one EA database seeded by <see cref="TwoTenantSeeder"/>.
/// </summary>
/// <remarks>
/// Use it as an xUnit class fixture: build one host per test class, and keep tests that change data
/// to rows they create.
/// </remarks>
public class TwoTenantApi : IDisposable
{
    private readonly CustomWebApplicationDbContextFactory<Program> _factory;

    public TwoTenantApi() : this(TenantSigning.Distinct)
    {
    }

    protected TwoTenantApi(TenantSigning signing)
    {
        Signing = signing;
        IntegrationTestServices.ApplyHostEnvironment();
        var tenantConfigDatabaseName = $"tenantconfig-{Guid.NewGuid()}";
        var tenants = new TestTenantConfigurationProvider(
            [TestTenant.A.ToConfiguration(signing), TestTenant.B.ToConfiguration(signing)]);

        _factory = new CustomWebApplicationDbContextFactory<Program>
        {
            SeedData = new Dictionary<Type, Action<DbContext>>
            {
                { typeof(ExternalApplicationsContext), ctx => TwoTenantSeeder.Seed((ExternalApplicationsContext)ctx) },
            },
            ExternalServicesConfiguration = services =>
            {
                services.RemoveAll<ITenantConfigurationProvider>();
                services.AddSingleton<ITenantConfigurationProvider>(tenants);
                services.RemoveAll<ITenantConfigurationRefreshState>();
                services.AddSingleton<ITenantConfigurationRefreshState>(new TestTenantConfigurationRefreshState());

                // The identity provider is out of scope: accept the test subject tokens instead of DfE Sign-In.
                services.RemoveAll<IExternalIdentityValidator>();
                services.AddTransient<IExternalIdentityValidator, TestExternalIdentityValidator>();

                IntegrationTestServices.ReplaceExternalServices(services, tenantConfigDatabaseName);
                ConfigureTestServices(services);
            },
        };
    }

    public TenantSigning Signing { get; }

    /// <summary>
    /// Extra test-only services for a derived fixture. Runs when the host is built, after the shared
    /// replacements, so it can wrap or add registrations.
    /// </summary>
    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
    }

    public IServiceProvider Services => _factory.Services;

    /// <summary>The in-process server, for clients that need its handler (the SignalR client).</summary>
    public TestServer Server => _factory.Server;

    /// <summary>
    /// A client for <paramref name="tenant"/> (<c>X-Tenant-ID</c>), with an optional bearer token or API key.
    /// The base factory adds no default headers, so a client with neither sends no credentials at all.
    /// </summary>
    public HttpClient Client(TestTenant tenant, string? bearerToken = null, string? apiKey = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TenantResolutionMiddleware.TenantIdHeader, tenant.Id.ToString());
        if (bearerToken is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        if (apiKey is not null)
            client.DefaultRequestHeaders.Add(AuthConstants.ApiKeyHeader, apiKey);
        return client;
    }

    /// <summary>
    /// Signs <paramref name="email"/> in to <paramref name="tenant"/> through token exchange and returns the
    /// user JWT the API issued. Throws if the exchange is refused.
    /// </summary>
    public async Task<string> SignInAsync(TestTenant tenant, string email)
    {
        using var response = await ExchangeAsync(tenant, email);
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<ExchangeTokenDto>();
        return token?.AccessToken ?? throw new InvalidOperationException($"Exchange for {email} returned no token.");
    }

    /// <summary>Calls token exchange as Web does for <paramref name="tenant"/> and returns the raw response.</summary>
    public async Task<HttpResponseMessage> ExchangeAsync(TestTenant tenant, string email, string? serviceApiKey = null)
    {
        using var client = Client(tenant, apiKey: serviceApiKey ?? tenant.ServiceApiKey);
        return await client.PostAsJsonAsync(
            "v1/tokens/exchange",
            new ExchangeTokenRequest(TestExternalIdentityValidator.CreateToken(email)));
    }

    public void Dispose()
    {
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary><see cref="TwoTenantApi"/> with one signing key, issuer and audience for both tenants.</summary>
public sealed class TwoTenantApiWithSharedSigning() : TwoTenantApi(TenantSigning.Shared);
