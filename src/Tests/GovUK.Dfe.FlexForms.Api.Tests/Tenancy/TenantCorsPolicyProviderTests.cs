using GovUK.Dfe.FlexForms.Api.Tenancy;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Tenancy;

public class TenantCorsPolicyProviderTests
{
    private TenantCorsPolicyProvider CreateProvider(string environmentName = "Production")
    {
        var options = Options.Create(new CorsOptions());
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        return new TenantCorsPolicyProvider(options, environment);
    }

    private DefaultHttpContext CreateHttpContext(TenantConfiguration? tenant)
    {
        var context = new DefaultHttpContext();
        var services = new ServiceCollection();
        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.CurrentTenant.Returns(tenant);
        services.AddScoped<ITenantContextAccessor>(_ => accessor);
        context.RequestServices = services.BuildServiceProvider();
        return context;
    }

    [Fact]
    public async Task GetPolicyAsync_ShouldReturnTenantOrigins_WhenFrontendPolicyAndTenantHasOrigins()
    {
        // Arrange
        var provider = CreateProvider();
        var tenant = new TenantConfiguration(
            Guid.NewGuid(), "TestTenant",
            new ConfigurationBuilder().Build(),
            new[] { "https://app1.example.com", "https://app2.example.com" });

        var context = CreateHttpContext(tenant);

        // Act
        var policy = await provider.GetPolicyAsync(context, "Frontend");

        // Assert
        Assert.NotNull(policy);
        Assert.Contains("https://app1.example.com", policy.Origins);
        Assert.Contains("https://app2.example.com", policy.Origins);
        Assert.True(policy.SupportsCredentials);
    }

    [Fact]
    public async Task GetPolicyAsync_ShouldFallbackToDefault_WhenTenantHasNoOrigins()
    {
        // Arrange
        var provider = CreateProvider();
        var tenant = new TenantConfiguration(
            Guid.NewGuid(), "TestTenant",
            new ConfigurationBuilder().Build(),
            Array.Empty<string>());

        var context = CreateHttpContext(tenant);

        // Act
        var policy = await provider.GetPolicyAsync(context, "Frontend");

        // Assert - default policy returns null when no named policy is configured
        Assert.Null(policy);
    }

    [Fact]
    public async Task GetPolicyAsync_ShouldFallbackToDefault_WhenNoTenantContext()
    {
        // Arrange
        var provider = CreateProvider();
        var context = CreateHttpContext(null);

        // Act
        var policy = await provider.GetPolicyAsync(context, "Frontend");

        // Assert
        Assert.Null(policy);
    }

    [Fact]
    public async Task GetPolicyAsync_ShouldNotAllowOriginRegisteredToAnotherTenant()
    {
        var tenantB = new TenantConfiguration(
            Guid.NewGuid(), "TenantB",
            new ConfigurationBuilder().Build(),
            ["https://b.example.com"]);

        var context = CreateHttpContext(tenantB);
        context.Request.Headers.Origin = "https://a.example.com";

        var policy = await CreateProvider().GetPolicyAsync(context, "Frontend");

        Assert.NotNull(policy);
        Assert.DoesNotContain("https://a.example.com", policy!.Origins);
        Assert.Contains("https://b.example.com", policy.Origins);
    }

    [Fact]
    public async Task GetPolicyAsync_ShouldFallbackToDefault_WhenPolicyNameIsNotFrontend()
    {
        // Arrange
        var provider = CreateProvider();
        var tenant = new TenantConfiguration(
            Guid.NewGuid(), "TestTenant",
            new ConfigurationBuilder().Build(),
            new[] { "https://app.example.com" });

        var context = CreateHttpContext(tenant);

        // Act
        var policy = await provider.GetPolicyAsync(context, "SomeOtherPolicy");

        // Assert
        Assert.Null(policy);
    }
}
