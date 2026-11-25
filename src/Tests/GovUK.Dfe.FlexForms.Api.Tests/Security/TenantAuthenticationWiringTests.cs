using System.IdentityModel.Tokens.Jwt;
using GovUK.Dfe.FlexForms.Api.Security;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Security;

/// <summary>
/// <see cref="AuthorizationExtensions"/> is excluded from coverage, so these pin that each
/// authentication handler is still wired to the rules tested in <see cref="TenantAuthenticationRulesTests"/>.
/// </summary>
public class TenantAuthenticationWiringTests
{
    private const string Issuer = "https://issuer.example/";
    private const string Audience = "api://flexforms";

    private readonly ITenantAuthProviderRegistry _registry = Substitute.For<ITenantAuthProviderRegistry>();
    private readonly ServiceProvider _services;

    public TenantAuthenticationWiringTests()
    {
        var tenants = Substitute.For<ITenantConfigurationProvider>();
        tenants.GetAllTenants().Returns(new List<TenantConfiguration>().AsReadOnly());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_registry);
        services.AddCustomAuthorization(new ConfigurationBuilder().Build(), tenants);
        _services = services.BuildServiceProvider();
    }

    [Fact]
    public void CompositeScheme_SelectsWithTheTenantRules()
    {
        var options = _services.GetRequiredService<IOptionsMonitor<PolicySchemeOptions>>().Get(AuthConstants.CompositeScheme);

        Assert.Equal(new Func<HttpContext, string?>(TenantAuthenticationRules.SelectScheme), options.ForwardDefaultSelector);
    }

    [Fact]
    public void TenantBearer_ChecksTenantConsistencyOnValidatedTokens()
    {
        var options = TenantBearerOptions();

        Assert.Equal(
            new Func<TokenValidatedContext, Task>(TenantAuthenticationRules.EnforceTenantConsistencyAsync),
            options.Events.OnTokenValidated);
    }

    [Fact]
    public void Mtls_ResolvesTheTenantFromTheCertificate()
    {
        var options = _services.GetRequiredService<IOptionsMonitor<CertificateAuthenticationOptions>>().Get(AuthConstants.Mtls);

        Assert.Equal(
            new Func<CertificateValidatedContext, Task>(TenantAuthenticationRules.OnCertificateValidated),
            options.Events.OnCertificateValidated);
    }

    [Fact]
    public void TenantBearer_AudienceValidator_UsesTheRequestTenant()
    {
        var tenant = new TenantConfiguration(Guid.NewGuid(), "Transfers", new ConfigurationBuilder().Build(), []);
        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.CurrentTenant.Returns(tenant);
        var requestServices = new ServiceCollection().AddSingleton(accessor).BuildServiceProvider();
        _services.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { RequestServices = requestServices };
        string[] audiences = [Audience];
        _registry.IsJwtAudienceValidForTenant(Issuer, audiences, tenant.Id, null).Returns(true);

        var valid = TenantBearerOptions().TokenValidationParameters.AudienceValidator!(
            audiences, new JwtSecurityToken(issuer: Issuer, audience: Audience), new TokenValidationParameters());

        Assert.True(valid);
    }

    [Fact]
    public void TenantBearer_AudienceValidator_WithoutARequest_UsesTheAnyTenantCheck()
    {
        _services.GetRequiredService<IHttpContextAccessor>().HttpContext = null;
        string[] audiences = [Audience];
        _registry.IsJwtAudienceValidForIssuerAnyTenant(Issuer, audiences).Returns(true);

        var valid = TenantBearerOptions().TokenValidationParameters.AudienceValidator!(
            audiences, new JwtSecurityToken(issuer: Issuer, audience: Audience), new TokenValidationParameters());

        Assert.True(valid);
        _registry.DidNotReceiveWithAnyArgs().IsJwtAudienceValidForTenant(default!, default!, default, default);
    }

    private JwtBearerOptions TenantBearerOptions() =>
        _services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(AuthConstants.TenantBearer);
}
