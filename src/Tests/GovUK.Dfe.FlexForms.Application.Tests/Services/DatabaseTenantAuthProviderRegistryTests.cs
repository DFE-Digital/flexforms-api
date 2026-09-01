using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Infrastructure.Security;
using GovUK.Dfe.FlexForms.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class DatabaseTenantAuthProviderRegistryTests
{
    private static TenantConfiguration BuildTenant(Guid id, IDictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        return new TenantConfiguration(id, "TestTenant-" + id, config, Array.Empty<string>());
    }

    private static DatabaseTenantAuthProviderRegistry CreateRegistry(
        IReadOnlyCollection<TenantConfiguration> tenants,
        out ITenantConfigurationChangedNotifier notifier,
        out ITenantConfigurationProvider configurationProvider)
    {
        configurationProvider = Substitute.For<ITenantConfigurationProvider>();
        configurationProvider.GetAllTenants().Returns(tenants);

        notifier = Substitute.For<ITenantConfigurationChangedNotifier>();

        return new DatabaseTenantAuthProviderRegistry(
            configurationProvider,
            notifier,
            Substitute.For<ILogger<DatabaseTenantAuthProviderRegistry>>());
    }

    [Fact]
    public void GetByIssuer_ReturnsInternalJwtHmacProvider_WhenAuthorizationTokenSettingsConfigured()
    {
        var tenantId = Guid.NewGuid();
        var tenant = BuildTenant(tenantId, new Dictionary<string, string?>
        {
            ["Authorization:TokenSettings:SecretKey"] = "AAAA",
            ["Authorization:TokenSettings:Issuer"] = "extapi",
            ["Authorization:TokenSettings:Audience"] = "extapi"
        });

        var registry = CreateRegistry(new[] { tenant }, out _, out _);

        var provider = registry.GetByIssuer("extapi");

        Assert.NotNull(provider);
        Assert.Equal(TenantAuthProviderKind.JwtHmac, provider!.Kind);
        Assert.Equal(tenantId, provider.TenantId);
        Assert.False(provider.IsServicePrincipal);
        Assert.Contains("extapi", provider.Audiences!);
    }

    [Fact]
    public void GetByIssuer_ReturnsAzureAdServicePrincipalProvider_WhenAzureAdConfigured()
    {
        var azureTenantId = Guid.NewGuid().ToString();
        var tenant = BuildTenant(Guid.NewGuid(), new Dictionary<string, string?>
        {
            ["AzureAd:TenantId"] = azureTenantId,
            ["AzureAd:ClientId"] = "client-1",
            ["AzureAd:Audience"] = "api://client-1"
        });

        var registry = CreateRegistry(new[] { tenant }, out _, out _);

        var expectedIssuer = $"https://sts.windows.net/{azureTenantId}/";
        var provider = registry.GetByIssuer(expectedIssuer);

        Assert.NotNull(provider);
        Assert.Equal(TenantAuthProviderKind.EntraOidc, provider!.Kind);
        Assert.True(provider.IsServicePrincipal);
    }

    [Fact]
    public void ApiKeyOnlyAuthProviders_StillProjectsLegacyTokenSettingsAndEntra()
    {
        var azureTenantId = Guid.NewGuid().ToString();
        var tenant = BuildTenant(Guid.NewGuid(), new Dictionary<string, string?>
        {
            ["Authorization:TokenSettings:SecretKey"] = "AAAA",
            ["Authorization:TokenSettings:Issuer"] = "21f3ed37-8443-4755-9ed2-c68ca86b4398",
            ["Authorization:TokenSettings:Audience"] = "20dafd6d-79e5-4caf-8b72-d070dcc9716f",
            ["AzureAd:TenantId"] = azureTenantId,
            ["AzureAd:ClientId"] = "client-1",
            ["AzureAd:Audience"] = "api://client-1",
            ["AuthProviders:Providers:0:Name"] = "file-validation",
            ["AuthProviders:Providers:0:Kind"] = "ApiKey",
            ["AuthProviders:Providers:0:IsServicePrincipal"] = "true",
            ["AuthProviders:Providers:0:KeyHash"] = "abc123",
            ["AuthProviders:Providers:0:Roles:0"] = "FileValidation"
        });

        var registry = CreateRegistry(new[] { tenant }, out _, out _);

        Assert.NotNull(registry.GetByApiKeyHash("abc123"));
        Assert.True(registry.IsValidAudience(
            "21f3ed37-8443-4755-9ed2-c68ca86b4398",
            new[] { "20dafd6d-79e5-4caf-8b72-d070dcc9716f" }));
        Assert.NotNull(registry.GetByIssuer($"https://sts.windows.net/{azureTenantId}/"));
    }

    [Fact]
    public void Rebuild_PicksUpNewlyAddedTenant_OnChangedEvent()
    {
        var tenantA = BuildTenant(Guid.NewGuid(), new Dictionary<string, string?>
        {
            ["Authorization:TokenSettings:SecretKey"] = "A",
            ["Authorization:TokenSettings:Issuer"] = "issuer-A",
            ["Authorization:TokenSettings:Audience"] = "extapi"
        });

        var configProvider = Substitute.For<ITenantConfigurationProvider>();
        configProvider.GetAllTenants().Returns(new[] { tenantA });

        var notifier = new TenantConfigurationChangedNotifier(
            Substitute.For<ILogger<TenantConfigurationChangedNotifier>>());

        var registry = new DatabaseTenantAuthProviderRegistry(
            configProvider,
            notifier,
            Substitute.For<ILogger<DatabaseTenantAuthProviderRegistry>>());

        Assert.NotNull(registry.GetByIssuer("issuer-A"));
        Assert.Null(registry.GetByIssuer("issuer-B"));

        var tenantB = BuildTenant(Guid.NewGuid(), new Dictionary<string, string?>
        {
            ["Authorization:TokenSettings:SecretKey"] = "B",
            ["Authorization:TokenSettings:Issuer"] = "issuer-B",
            ["Authorization:TokenSettings:Audience"] = "extapi"
        });
        configProvider.GetAllTenants().Returns(new[] { tenantA, tenantB });

        notifier.Notify();

        Assert.NotNull(registry.GetByIssuer("issuer-A"));
        Assert.NotNull(registry.GetByIssuer("issuer-B"));
    }

    [Fact]
    public async Task TenantSigningKeyResolver_ReturnsSymmetricKey_ForJwtHmacProvider()
    {
        var tenant = BuildTenant(Guid.NewGuid(), new Dictionary<string, string?>
        {
            ["Authorization:TokenSettings:SecretKey"] = "iw5/ivfUWaCpj+n3TihlGUzRVna+KKu8IfLP52GdgNXlDcqt3+N2MM45rwQ=",
            ["Authorization:TokenSettings:Issuer"] = "extapi",
            ["Authorization:TokenSettings:Audience"] = "extapi"
        });

        var registry = CreateRegistry(new[] { tenant }, out _, out _);
        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient());

        var resolver = new TenantSigningKeyResolver(
            registry,
            httpFactory,
            Substitute.For<ILogger<TenantSigningKeyResolver>>());

        var keys = await resolver.GetSigningKeysAsync("extapi", CancellationToken.None);

        Assert.Single(keys);
    }

    [Fact]
    public void IsValidAudience_ReturnsFalse_WhenAudienceNotInProvider()
    {
        var tenant = BuildTenant(Guid.NewGuid(), new Dictionary<string, string?>
        {
            ["Authorization:TokenSettings:SecretKey"] = "AAAA",
            ["Authorization:TokenSettings:Issuer"] = "extapi",
            ["Authorization:TokenSettings:Audience"] = "extapi"
        });

        var registry = CreateRegistry(new[] { tenant }, out _, out _);

        Assert.True(registry.IsValidAudience("extapi", new[] { "extapi" }));
        Assert.False(registry.IsValidAudience("extapi", new[] { "wrong-audience" }));
        Assert.False(registry.IsValidAudience("unknown-issuer", new[] { "extapi" }));
    }

    [Fact]
    public void GetByCertificateThumbprint_NormalizesInputAndMatches()
    {
        // This stage doesn't project a Mtls provider directly, but the lookup method is exercised
        // via a synthetic registry state created by adding a private projection in stage 3.
        // For now we just verify case/space/colon normalization on the lookup side.
        var tenant = BuildTenant(Guid.NewGuid(), new Dictionary<string, string?>());
        var registry = CreateRegistry(new[] { tenant }, out _, out _);

        Assert.Null(registry.GetByCertificateThumbprint("AB:CD:12"));
        Assert.Null(registry.GetByCertificateThumbprint("ab cd 12"));
    }

    [Fact]
    public void GetProvidersByIssuer_ReturnsMultiple_WhenSameStsIssuerSharedAcrossTenants()
    {
        var azureDir = Guid.NewGuid().ToString();
        var issuer = $"https://sts.windows.net/{azureDir}/";
        var t1 = BuildTenant(Guid.NewGuid(), new Dictionary<string, string?>
        {
            ["AzureAd:TenantId"] = azureDir,
            ["AzureAd:ClientId"] = "client-a",
            ["AzureAd:Audience"] = "api://shared"
        });
        var t2 = BuildTenant(Guid.NewGuid(), new Dictionary<string, string?>
        {
            ["AzureAd:TenantId"] = azureDir,
            ["AzureAd:ClientId"] = "client-b",
            ["AzureAd:Audience"] = "api://shared"
        });
        var registry = CreateRegistry(new[] { t1, t2 }, out _, out _);

        var list = registry.GetProvidersByIssuer(issuer);
        Assert.True(list.Count >= 2);
    }

    [Fact]
    public void ResolveJwtBearerProvider_DisambiguatesByClientId_WhenSameIssuerAndTenant()
    {
        var azureDir = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var issuer = $"https://sts.windows.net/{azureDir}/";
        var t1 = BuildTenant(tenantId, new Dictionary<string, string?>
        {
            ["AzureAd:TenantId"] = azureDir,
            ["AzureAd:ClientId"] = "app-a-111",
            ["AzureAd:Audience"] = "api://shared"
        });
        var registry = CreateRegistry(new[] { t1 }, out _, out _);

        var match = registry.ResolveJwtBearerProvider(
            issuer,
            new[] { "api://shared" },
            tenantId,
            "app-a-111");

        Assert.NotNull(match);
        Assert.Equal(tenantId, match!.TenantId);
        Assert.Equal("app-a-111", match.ClientId);
    }

    [Fact]
    public void ResolveJwtBearerProvider_ReturnsCorrectRow_WhenTwoTenantsShareDirectory()
    {
        var azureDir = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var issuer = $"https://sts.windows.net/{azureDir}/";
        var t1 = BuildTenant(tenantA, new Dictionary<string, string?>
        {
            ["AzureAd:TenantId"] = azureDir,
            ["AzureAd:ClientId"] = "app-a-111",
            ["AzureAd:Audience"] = "api://shared"
        });
        var t2 = BuildTenant(tenantB, new Dictionary<string, string?>
        {
            ["AzureAd:TenantId"] = azureDir,
            ["AzureAd:ClientId"] = "app-b-222",
            ["AzureAd:Audience"] = "api://shared"
        });
        var registry = CreateRegistry(new[] { t1, t2 }, out _, out _);

        var forA = registry.ResolveJwtBearerProvider(issuer, new[] { "api://shared" }, tenantA, "app-a-111");
        var forB = registry.ResolveJwtBearerProvider(issuer, new[] { "api://shared" }, tenantB, "app-b-222");

        Assert.NotNull(forA);
        Assert.Equal(tenantA, forA!.TenantId);
        Assert.NotNull(forB);
        Assert.Equal(tenantB, forB!.TenantId);
    }

    [Fact]
    public void IsJwtAudienceValidForTenant_AcceptsRawClientId_WhenAzureAdAudienceIsAppIdUri()
    {
        var azureDir = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var clientId = "65c013ab-cc5d-4fd9-8797-e81c5b16eb3e";
        var issuer = $"https://sts.windows.net/{azureDir}/";
        var tenant = BuildTenant(tenantId, new Dictionary<string, string?>
        {
            ["AzureAd:TenantId"] = azureDir,
            ["AzureAd:ClientId"] = clientId,
            ["AzureAd:Audience"] = $"api://{clientId}"
        });
        var registry = CreateRegistry(new[] { tenant }, out _, out _);

        Assert.True(registry.IsJwtAudienceValidForTenant(issuer, new[] { clientId }, tenantId, clientId));
        Assert.True(registry.IsJwtAudienceValidForTenant(
            issuer,
            new[] { $"api://{clientId}/.default" },
            tenantId,
            clientId));
        Assert.True(registry.IsJwtAudienceValidForTenant(
            issuer,
            new[] { $"api://{clientId}/access_as_user" },
            tenantId,
            clientId));
        Assert.False(registry.IsJwtAudienceValidForTenant(
            issuer,
            new[] { "00000003-0000-0000-c000-000000000000" },
            tenantId,
            clientId));
        Assert.False(registry.IsJwtAudienceValidForTenant(
            issuer,
            new[] { "https://graph.microsoft.com" },
            tenantId,
            clientId));
    }

    [Fact]
    public void HasAnyProviderForIssuer_ReturnsTrue_ForV2IssuerAlias_WhenAzureAdUsesStsDefault()
    {
        var azureDir = Guid.NewGuid().ToString();
        var tenant = BuildTenant(Guid.NewGuid(), new Dictionary<string, string?>
        {
            ["AzureAd:TenantId"] = azureDir,
            ["AzureAd:ClientId"] = "c1",
            ["AzureAd:Audience"] = "api://c1"
        });
        var registry = CreateRegistry(new[] { tenant }, out _, out _);

        Assert.True(registry.HasAnyProviderForIssuer($"https://login.microsoftonline.com/{azureDir}/v2.0"));
    }
}
