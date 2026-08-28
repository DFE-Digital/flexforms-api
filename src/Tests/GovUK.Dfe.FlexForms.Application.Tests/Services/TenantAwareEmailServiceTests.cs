using GovUK.Dfe.CoreLibs.Email.Interfaces;
using GovUK.Dfe.CoreLibs.Email.Models;
using GovUK.Dfe.FlexForms.Application.Services;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class TenantAwareEmailServiceTests
{
    private readonly IEmailService _tenantEmail;
    private readonly ITenantEmailServiceFactory _tenantEmailFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TenantAwareEmailService _service;

    public TenantAwareEmailServiceTests()
    {
        _tenantEmail = Substitute.For<IEmailService>();
        _tenantEmailFactory = Substitute.For<ITenantEmailServiceFactory>();
        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        _httpContextAccessor.HttpContext.Returns(new DefaultHttpContext());

        _tenantEmailFactory.GetRequiredEmailService().Returns(_tenantEmail);
        _service = new TenantAwareEmailService(_tenantEmailFactory, _httpContextAccessor);
    }

    [Fact]
    public async Task SendEmailAsync_ShouldUseTenantFactory()
    {
        var message = new EmailMessage { ToEmail = "a@b.com", TemplateId = "t1" };
        var expected = new EmailResponse { Id = "1", Status = EmailStatus.Sent };
        _tenantEmail.SendEmailAsync(message, Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _service.SendEmailAsync(message);

        Assert.Same(expected, result);
        await _tenantEmail.Received(1).SendEmailAsync(message, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendEmailAsync_ShouldPropagateFactoryFailure_WhenNoTenantContext()
    {
        _tenantEmailFactory.GetRequiredEmailService()
            .Returns(_ => throw new InvalidOperationException("No tenant context available for email operation."));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SendEmailAsync(new EmailMessage { ToEmail = "a@b.com" }));

        Assert.Contains("No tenant context", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendEmailAsync_ShouldPropagateFactoryFailure_WhenEmailProviderMissing()
    {
        _tenantEmailFactory.GetRequiredEmailService()
            .Returns(_ => throw new InvalidOperationException(
                "Tenant 'BrokenTenant' has no Email:Provider configured."));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SendEmailAsync(new EmailMessage { ToEmail = "a@b.com" }));

        Assert.Contains("Email:Provider", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendEmailAsync_ShouldPropagateFactoryFailure_WhenGovUkNotifyApiKeyMissing()
    {
        _tenantEmailFactory.GetRequiredEmailService()
            .Returns(_ => throw new InvalidOperationException(
                "Tenant 'BrokenTenant' Email Provider is GovUkNotify but Email:GovUkNotify:ApiKey is missing."));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SendEmailAsync(new EmailMessage { ToEmail = "a@b.com" }));

        Assert.Contains("ApiKey", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendEmailAsync_ShouldThrow_WhenNoHttpContext()
    {
        _httpContextAccessor.HttpContext.Returns((HttpContext?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SendEmailAsync(new EmailMessage { ToEmail = "a@b.com" }));

        _tenantEmailFactory.DidNotReceive().GetRequiredEmailService();
    }
}
