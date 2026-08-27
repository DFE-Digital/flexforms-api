using GovUK.Dfe.CoreLibs.Email.Interfaces;
using GovUK.Dfe.CoreLibs.Email.Models;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class TenantAwareEmailServiceTests
{
    private readonly IEmailService _inner;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly TenantAwareEmailService _service;

    public TenantAwareEmailServiceTests()
    {
        _inner = Substitute.For<IEmailService>();
        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        _tenantContextAccessor = Substitute.For<ITenantContextAccessor>();

        var services = new ServiceCollection();
        services.AddSingleton(_tenantContextAccessor);
        var serviceProvider = services.BuildServiceProvider();
        _httpContextAccessor.HttpContext.Returns(new DefaultHttpContext { RequestServices = serviceProvider });

        _service = new TenantAwareEmailService(_inner, _httpContextAccessor);
    }

    private static TenantConfiguration CreateTenantWithEmail()
    {
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Provider"] = "GovUkNotify",
                ["Email:GovUkNotify:ApiKey"] = "test-key",
                ["Email:ServiceSupportEmailAddress"] = "support@education.gov.uk"
            })
            .Build();

        return new TenantConfiguration(Guid.NewGuid(), "TestTenant", settings, Array.Empty<string>());
    }

    [Fact]
    public async Task SendEmailAsync_ShouldDelegate_WhenTenantEmailConfigured()
    {
        _tenantContextAccessor.CurrentTenant.Returns(CreateTenantWithEmail());
        var message = new EmailMessage { ToEmail = "a@b.com", TemplateId = "t1" };
        var expected = new EmailResponse { Id = "1", Status = EmailStatus.Sent };
        _inner.SendEmailAsync(message, Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _service.SendEmailAsync(message);

        Assert.Same(expected, result);
        await _inner.Received(1).SendEmailAsync(message, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendEmailAsync_ShouldThrow_WhenNoTenantContext()
    {
        _tenantContextAccessor.CurrentTenant.Returns((TenantConfiguration?)null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SendEmailAsync(new EmailMessage { ToEmail = "a@b.com" }));

        Assert.Contains("No tenant context", ex.Message, StringComparison.Ordinal);
        await _inner.DidNotReceiveWithAnyArgs().SendEmailAsync(default!, default);
    }

    [Fact]
    public async Task SendEmailAsync_ShouldThrow_WhenEmailProviderMissing()
    {
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:ServiceSupportEmailAddress"] = "support@education.gov.uk"
            })
            .Build();
        _tenantContextAccessor.CurrentTenant.Returns(
            new TenantConfiguration(Guid.NewGuid(), "BrokenTenant", settings, Array.Empty<string>()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SendEmailAsync(new EmailMessage { ToEmail = "a@b.com" }));

        Assert.Contains("Email:Provider", ex.Message, StringComparison.Ordinal);
        await _inner.DidNotReceiveWithAnyArgs().SendEmailAsync(default!, default);
    }

    [Fact]
    public async Task SendEmailAsync_ShouldThrow_WhenGovUkNotifyApiKeyMissing()
    {
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Provider"] = "GovUkNotify"
            })
            .Build();
        _tenantContextAccessor.CurrentTenant.Returns(
            new TenantConfiguration(Guid.NewGuid(), "BrokenTenant", settings, Array.Empty<string>()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SendEmailAsync(new EmailMessage { ToEmail = "a@b.com" }));

        Assert.Contains("ApiKey", ex.Message, StringComparison.Ordinal);
        await _inner.DidNotReceiveWithAnyArgs().SendEmailAsync(default!, default);
    }

    [Fact]
    public async Task SendEmailAsync_ShouldThrow_WhenNoHttpContext()
    {
        _httpContextAccessor.HttpContext.Returns((HttpContext?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SendEmailAsync(new EmailMessage { ToEmail = "a@b.com" }));

        await _inner.DidNotReceiveWithAnyArgs().SendEmailAsync(default!, default);
    }
}
