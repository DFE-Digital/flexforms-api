using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GovUK.Dfe.FlexForms.Api.Security;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Security;

public class TenantAuthenticationRulesTests
{
    private const string Issuer = "https://issuer.example/";
    private const string Audience = "api://flexforms";
    private const string ClientId = "client-app-id";

    private static readonly TenantConfiguration TenantA = Tenant("a1b2c3d4-1111-4111-8111-1111111111ab", "Transfers");
    private static readonly TenantConfiguration TenantB = Tenant("22222222-2222-4222-8222-222222222222", "Visits");

    private readonly ITenantAuthProviderRegistry _registry = Substitute.For<ITenantAuthProviderRegistry>();
    private readonly RecordingLogger _logger = new();

    // ---------- SelectScheme ----------

    [Fact]
    public void SelectScheme_HubsPath_UsesHubCookie_EvenWithApiKeyAndCertificate()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/hubs/notifications";
        context.Request.Headers[AuthConstants.ApiKeyHeader] = "key";
        context.Connection.ClientCertificate = Certificate();

        Assert.Equal("HubCookie", TenantAuthenticationRules.SelectScheme(context));
    }

    [Fact]
    public void SelectScheme_ApiKeyHeader_WinsOverCertificate()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/v1/Applications";
        context.Request.Headers[AuthConstants.ApiKeyHeader] = "key";
        context.Connection.ClientCertificate = Certificate();

        Assert.Equal(AuthConstants.ApiKey, TenantAuthenticationRules.SelectScheme(context));
    }

    [Fact]
    public void SelectScheme_ClientCertificate_WinsOverPlatformBootstrapPath()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/v1/host-config";
        context.Connection.ClientCertificate = Certificate();

        Assert.Equal(AuthConstants.Mtls, TenantAuthenticationRules.SelectScheme(context));
    }

    [Fact]
    public void SelectScheme_PlatformBootstrapPath_UsesPlatformBearer()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/v1/host-config";

        Assert.Equal(AuthConstants.PlatformBearer, TenantAuthenticationRules.SelectScheme(context));
    }

    [Theory]
    [InlineData("/v1/Applications")]
    [InlineData("/hubsetup")]
    public void SelectScheme_Otherwise_UsesTenantBearer(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        Assert.Equal(AuthConstants.TenantBearer, TenantAuthenticationRules.SelectScheme(context));
    }

    // ---------- IsAudienceValid ----------

    [Fact]
    public void IsAudienceValid_NoToken_IsFalse_WithoutAskingTheRegistry()
    {
        Assert.False(TenantAuthenticationRules.IsAudienceValid([Audience], null, _registry, Accessor(TenantA)));
        Assert.Empty(_registry.ReceivedCalls());
    }

    [Fact]
    public void IsAudienceValid_TokenWithoutIssuer_IsFalse_WithoutAskingTheRegistry()
    {
        var token = new JwtSecurityToken(audience: Audience);

        Assert.False(TenantAuthenticationRules.IsAudienceValid([Audience], token, _registry, Accessor(null)));
        Assert.Empty(_registry.ReceivedCalls());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsAudienceValid_NoTenantResolved_UsesTheAnyTenantCheck(bool registryAnswer)
    {
        string[] audiences = [Audience];
        _registry.IsJwtAudienceValidForIssuerAnyTenant(Issuer, audiences).Returns(registryAnswer);

        var result = TenantAuthenticationRules.IsAudienceValid(audiences, Jwt(), _registry, Accessor(null));

        Assert.Equal(registryAnswer, result);
        _registry.DidNotReceiveWithAnyArgs().IsJwtAudienceValidForTenant(default!, default!, default, default);
    }

    [Fact]
    public void IsAudienceValid_NoTenantAccessor_UsesTheAnyTenantCheck()
    {
        string[] audiences = [Audience];
        _registry.IsJwtAudienceValidForIssuerAnyTenant(Issuer, audiences).Returns(true);

        Assert.True(TenantAuthenticationRules.IsAudienceValid(audiences, Jwt(), _registry, null));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsAudienceValid_TenantResolved_UsesTheSingleTenantCheck(bool registryAnswer)
    {
        string[] audiences = [Audience];
        _registry.IsJwtAudienceValidForTenant(Issuer, audiences, TenantA.Id, ClientId).Returns(registryAnswer);

        var result = TenantAuthenticationRules.IsAudienceValid(
            audiences, Jwt(new Claim("azp", ClientId)), _registry, Accessor(TenantA));

        Assert.Equal(registryAnswer, result);
        _registry.DidNotReceiveWithAnyArgs().IsJwtAudienceValidForIssuerAnyTenant(default!, default!);
    }

    public static TheoryData<Claim[], string?> ClientIdClaims => new()
    {
        { [new Claim("azp", "from-azp"), new Claim("appid", "from-appid")], "from-azp" },
        { [new Claim("appid", "from-appid"), new Claim(EntraClientIdentity.AppIdSchemaClaimType, "from-schema")], "from-appid" },
        { [new Claim(EntraClientIdentity.AppIdSchemaClaimType, "from-schema")], "from-schema" },
        { [new Claim("azp", ""), new Claim("appid", "from-appid")], "from-appid" },
        { [], null },
    };

    [Theory]
    [MemberData(nameof(ClientIdClaims))]
    public void IsAudienceValid_PassesTheCallingAppId_AzpThenAppId(Claim[] claims, string? expectedClientId)
    {
        TenantAuthenticationRules.IsAudienceValid([Audience], Jwt(claims), _registry, Accessor(TenantA));

        _registry.Received(1).IsJwtAudienceValidForTenant(
            Issuer, Arg.Any<IEnumerable<string>>(), TenantA.Id, expectedClientId);
    }

    // ---------- EnforceTenantConsistencyAsync ----------

    [Fact]
    public async Task EnforceTenantConsistency_NoTenantResolved_Allows_WithoutAskingTheRegistry()
    {
        var context = TokenContext(tenant: null, Jwt(new Claim(TenantAuthClaimTypes.TenantId, TenantB.Id.ToString())));

        await TenantAuthenticationRules.EnforceTenantConsistencyAsync(context);

        Assert.Null(context.Result);
        Assert.Empty(_registry.ReceivedCalls());
        Assert.Null(StashedProvider(context.HttpContext));
    }

    [Fact]
    public async Task EnforceTenantConsistency_MatchingTenantId_Allows_AndStashesTheProvider()
    {
        var provider = Provider(TenantA);
        _registry.ResolveJwtBearerProvider(Issuer, Arg.Any<IEnumerable<string>>(), TenantA.Id, null).Returns(provider);
        // Claim casing differs from Guid.ToString() - the comparison is case-insensitive.
        var context = TokenContext(TenantA, Jwt(new Claim(TenantAuthClaimTypes.TenantId, TenantA.Id.ToString().ToUpperInvariant())));

        await TenantAuthenticationRules.EnforceTenantConsistencyAsync(context);

        Assert.Null(context.Result);
        Assert.Same(provider, StashedProvider(context.HttpContext));
    }

    [Fact]
    public async Task EnforceTenantConsistency_MismatchedTenantId_Fails()
    {
        _registry.ResolveJwtBearerProvider(default!, default!, default, default).ReturnsForAnyArgs(Provider(TenantA));
        var context = TokenContext(TenantA, Jwt(new Claim(TenantAuthClaimTypes.TenantId, TenantB.Id.ToString())));

        await TenantAuthenticationRules.EnforceTenantConsistencyAsync(context);

        var failure = Assert.IsType<AuthenticateResult>(context.Result).Failure;
        Assert.NotNull(failure);
        Assert.Contains(TenantB.Id.ToString(), failure.Message);
        Assert.Contains(TenantA.Id.ToString(), failure.Message);
    }

    [Fact]
    public async Task EnforceTenantConsistency_MismatchedTenantId_Fails_EvenWhenNoProviderMatched()
    {
        _registry.HasAnyProviderForIssuer(Issuer).Returns(false);
        var context = TokenContext(TenantA, Jwt(new Claim(TenantAuthClaimTypes.TenantId, TenantB.Id.ToString())));

        await TenantAuthenticationRules.EnforceTenantConsistencyAsync(context);

        Assert.NotNull(context.Result?.Failure);
    }

    [Fact]
    public async Task EnforceTenantConsistency_NoTenantIdClaim_OneProviderMatched_Allows_AndStashesIt()
    {
        var provider = Provider(TenantA);
        _registry.ResolveJwtBearerProvider(Issuer, Arg.Any<IEnumerable<string>>(), TenantA.Id, ClientId).Returns(provider);
        var context = TokenContext(TenantA, Jwt(new Claim("azp", ClientId)));

        await TenantAuthenticationRules.EnforceTenantConsistencyAsync(context);

        Assert.Null(context.Result);
        Assert.Same(provider, StashedProvider(context.HttpContext));
        _registry.Received(1).ResolveJwtBearerProvider(
            Issuer, Arg.Is<IEnumerable<string>>(a => a.SequenceEqual(new[] { Audience })), TenantA.Id, ClientId);
        Assert.Empty(_logger.Warnings);
    }

    [Fact]
    public async Task EnforceTenantConsistency_NoTenantIdClaim_NoUniqueProvider_KnownIssuer_Fails()
    {
        // The registry returns null when zero or several providers fit the issuer, audience, app id and tenant.
        _registry.ResolveJwtBearerProvider(default!, default!, default, default).ReturnsForAnyArgs((TenantAuthProvider?)null);
        _registry.HasAnyProviderForIssuer(Issuer).Returns(true);
        var context = TokenContext(TenantA, Jwt());

        await TenantAuthenticationRules.EnforceTenantConsistencyAsync(context);

        var failure = context.Result?.Failure;
        Assert.NotNull(failure);
        Assert.Contains(Issuer, failure.Message);
        Assert.Null(StashedProvider(context.HttpContext));
    }

    [Fact]
    public async Task EnforceTenantConsistency_NoTenantIdClaim_UnknownIssuer_AllowsWithAWarning()
    {
        _registry.HasAnyProviderForIssuer(Issuer).Returns(false);
        var context = TokenContext(TenantA, Jwt());

        await TenantAuthenticationRules.EnforceTenantConsistencyAsync(context);

        Assert.Null(context.Result);
        Assert.Null(StashedProvider(context.HttpContext));
        var warning = Assert.Single(_logger.Warnings);
        Assert.Contains(Issuer, warning);
    }

    [Fact]
    public async Task EnforceTenantConsistency_UnknownIssuer_WithoutALogger_StillAllows()
    {
        _registry.HasAnyProviderForIssuer(Issuer).Returns(false);
        var context = TokenContext(TenantA, Jwt(), registerLogger: false);

        await TenantAuthenticationRules.EnforceTenantConsistencyAsync(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public async Task EnforceTenantConsistency_NoRegistry_AllowsWithAWarning()
    {
        var context = TokenContext(TenantA, Jwt(), registerRegistry: false);

        await TenantAuthenticationRules.EnforceTenantConsistencyAsync(context);

        Assert.Null(context.Result);
        Assert.Single(_logger.Warnings);
    }

    [Fact]
    public async Task EnforceTenantConsistency_NonJwtToken_ReadsAudienceAndAppIdFromThePrincipal()
    {
        var provider = Provider(TenantA);
        _registry.ResolveJwtBearerProvider(default!, default!, default, default).ReturnsForAnyArgs(provider);
        var token = Substitute.For<SecurityToken>();
        token.Issuer.Returns(Issuer);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Aud, "aud-1"),
            new Claim(JwtRegisteredClaimNames.Aud, ""),
            new Claim(JwtRegisteredClaimNames.Aud, "aud-2"),
            new Claim("appid", ClientId),
        ], "test"));
        var context = TokenContext(TenantA, token, principal);

        await TenantAuthenticationRules.EnforceTenantConsistencyAsync(context);

        Assert.Null(context.Result);
        Assert.Same(provider, StashedProvider(context.HttpContext));
        _registry.Received(1).ResolveJwtBearerProvider(
            Issuer, Arg.Is<IEnumerable<string>>(a => a.SequenceEqual(new[] { "aud-1", "aud-2" })), TenantA.Id, ClientId);
    }

    [Fact]
    public async Task EnforceTenantConsistency_NoIssuer_SkipsTheRegistry_AndAllows()
    {
        var token = Substitute.For<SecurityToken>();
        token.Issuer.Returns((string?)null);
        var context = TokenContext(TenantA, token, principal: null);

        await TenantAuthenticationRules.EnforceTenantConsistencyAsync(context);

        Assert.Null(context.Result);
        _registry.DidNotReceiveWithAnyArgs().ResolveJwtBearerProvider(default!, default!, default, default);
        _registry.DidNotReceiveWithAnyArgs().HasAnyProviderForIssuer(default!);
    }

    // ---------- OnCertificateValidated ----------

    [Fact]
    public async Task OnCertificateValidated_UnknownThumbprint_Fails_AndLeavesThePrincipal()
    {
        using var certificate = Certificate();
        _registry.GetByCertificateThumbprint(certificate.Thumbprint).Returns((TenantAuthProvider?)null);
        var original = new ClaimsPrincipal(new ClaimsIdentity("Certificate"));
        var context = CertificateContext(TenantA, certificate, original);

        await TenantAuthenticationRules.OnCertificateValidated(context);

        Assert.Equal("Unknown client certificate.", context.Result?.Failure?.Message);
        Assert.Same(original, context.Principal);
        Assert.Null(StashedProvider(context.HttpContext));
    }

    [Fact]
    public async Task OnCertificateValidated_CertificateForAnotherTenant_Fails_WithAWarning()
    {
        using var certificate = Certificate();
        _registry.GetByCertificateThumbprint(certificate.Thumbprint).Returns(Provider(TenantB, TenantAuthProviderKind.Mtls));
        var context = CertificateContext(TenantA, certificate);

        await TenantAuthenticationRules.OnCertificateValidated(context);

        var failure = context.Result?.Failure;
        Assert.NotNull(failure);
        Assert.Contains(TenantB.Id.ToString(), failure.Message);
        Assert.Null(StashedProvider(context.HttpContext));
        Assert.Single(_logger.Warnings);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnCertificateValidated_KnownCertificate_BuildsTheTenantPrincipal(bool tenantResolved)
    {
        using var certificate = Certificate();
        var provider = Provider(TenantA, TenantAuthProviderKind.Mtls);
        _registry.GetByCertificateThumbprint(certificate.Thumbprint).Returns(provider);
        var original = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Thumbprint, certificate.Thumbprint)], "Certificate"));
        var context = CertificateContext(tenantResolved ? TenantA : null, certificate, original);

        await TenantAuthenticationRules.OnCertificateValidated(context);

        Assert.True(context.Result?.Succeeded);
        Assert.Same(provider, StashedProvider(context.HttpContext));
        var principal = context.Principal!;
        Assert.Equal(AuthConstants.Mtls, principal.Identity?.AuthenticationType);
        Assert.Equal(TenantA.Id.ToString(), principal.FindFirst(TenantAuthClaimTypes.TenantId)?.Value);
        Assert.Equal(certificate.Thumbprint, principal.FindFirst(ClaimTypes.Thumbprint)?.Value);
        Assert.Empty(_logger.Warnings);
    }

    // ---------- helpers ----------

    private static TenantConfiguration Tenant(string id, string name) =>
        new(Guid.Parse(id), name, new ConfigurationBuilder().Build(), []);

    private static ITenantContextAccessor Accessor(TenantConfiguration? tenant)
    {
        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.CurrentTenant.Returns(tenant);
        return accessor;
    }

    private static TenantAuthProvider Provider(TenantConfiguration tenant, TenantAuthProviderKind kind = TenantAuthProviderKind.EntraOidc) =>
        new(tenant.Id, "provider", kind, IsServicePrincipal: true, Issuer: Issuer, Audiences: [Audience], ClientId: ClientId);

    private static JwtSecurityToken Jwt(params Claim[] claims) =>
        new(issuer: Issuer, audience: Audience, claims: claims);

    private static X509Certificate2 Certificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=flexforms-test-client", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
    }

    private HttpContext Http(TenantConfiguration? tenant, bool registerRegistry = true, bool registerLogger = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Accessor(tenant));
        if (registerRegistry) services.AddSingleton(_registry);
        if (registerLogger) services.AddSingleton<ILogger<Program>>(_logger);
        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    private TokenValidatedContext TokenContext(
        TenantConfiguration? tenant,
        SecurityToken token,
        ClaimsPrincipal? principal = null,
        bool registerRegistry = true,
        bool registerLogger = true)
    {
        var scheme = new AuthenticationScheme(AuthConstants.TenantBearer, null, typeof(JwtBearerHandler));
        return new TokenValidatedContext(Http(tenant, registerRegistry, registerLogger), scheme, new JwtBearerOptions())
        {
            SecurityToken = token,
            Principal = principal ?? (token is JwtSecurityToken jwt
                ? new ClaimsPrincipal(new ClaimsIdentity(jwt.Claims, "test"))
                : null),
        };
    }

    private CertificateValidatedContext CertificateContext(
        TenantConfiguration? tenant,
        X509Certificate2 certificate,
        ClaimsPrincipal? principal = null)
    {
        // The handler type is only scheme metadata here; the certificate handler type is internal.
        var scheme = new AuthenticationScheme(AuthConstants.Mtls, null, typeof(JwtBearerHandler));
        return new CertificateValidatedContext(Http(tenant), scheme, new CertificateAuthenticationOptions())
        {
            ClientCertificate = certificate,
            Principal = principal ?? new ClaimsPrincipal(new ClaimsIdentity("Certificate")),
        };
    }

    private static TenantAuthProvider? StashedProvider(HttpContext context) =>
        context.Items.TryGetValue(AuthConstants.MatchedAuthProviderKey, out var value) ? value as TenantAuthProvider : null;

    private sealed class RecordingLogger : ILogger<Program>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings.Add(formatter(state, exception));
        }
    }
}
