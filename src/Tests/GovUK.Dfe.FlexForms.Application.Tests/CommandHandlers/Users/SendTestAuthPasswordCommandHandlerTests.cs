using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Email.Interfaces;
using GovUK.Dfe.CoreLibs.Email.Models;
using GovUK.Dfe.FlexForms.Application.Options;
using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Users.Commands;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.Users;

public class SendTestAuthPasswordCommandHandlerTests
{
    private const string Email = "tester@education.gov.uk";
    private const string NotifyTemplateId = "a94eca1d-0a88-4144-b895-ecc66aee6e56";
    private const string Password = "042917";

    private readonly ITenantContextAccessor _tenantContextAccessor = Substitute.For<ITenantContextAccessor>();
    private readonly IHostEnvironment _hostEnvironment = Substitute.For<IHostEnvironment>();
    private readonly ITestAuthPasswordService _passwordService = Substitute.For<ITestAuthPasswordService>();
    private readonly IEmailTemplateResolver _emailTemplateResolver = Substitute.For<IEmailTemplateResolver>();
    private readonly IEmailService _emailService = Substitute.For<IEmailService>();
    private readonly SendTestAuthPasswordCommandHandler _handler;

    public SendTestAuthPasswordCommandHandlerTests()
    {
        _hostEnvironment.EnvironmentName.Returns("Test");
        SetTenant(testAuthEnabled: true);
        _emailTemplateResolver.ResolveTenantEmailTemplateAsync(EmailTypes.TestAuthPassword).Returns(NotifyTemplateId);
        _passwordService.IssueAsync(Email).Returns(Password);
        _emailService.SendEmailAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(new EmailResponse { Id = "1", Status = EmailStatus.Sent });

        _handler = new SendTestAuthPasswordCommandHandler(
            NullLogger<SendTestAuthPasswordCommandHandler>.Instance,
            _tenantContextAccessor,
            _hostEnvironment,
            _passwordService,
            _emailTemplateResolver,
            _emailService);
    }

    [Fact]
    public async Task Handle_emails_password_using_TestAuthPasswordEmail_template()
    {
        var cancellationToken = new CancellationTokenSource().Token;

        var result = await _handler.Handle(new SendTestAuthPasswordCommand(Email), cancellationToken);

        Assert.True(result.IsSuccess);
        await _emailService.Received(1).SendEmailAsync(
            Arg.Is<EmailMessage>(m =>
                m.ToEmail == Email &&
                m.TemplateId == NotifyTemplateId &&
                m.Personalization!.Count == 3 &&
                (string)m.Personalization["environment"] == "Test" &&
                (string)m.Personalization["temp_password"] == Password &&
                (string)m.Personalization["service_name"] == "TestTenant"),
            cancellationToken);
    }

    [Fact]
    public async Task Handle_uses_tenant_layout_service_name_when_configured()
    {
        SetTenant(testAuthEnabled: true, serviceName: "  Transfer an academy  ");

        await _handler.Handle(new SendTestAuthPasswordCommand(Email), CancellationToken.None);

        await _emailService.Received(1).SendEmailAsync(
            Arg.Is<EmailMessage>(m => (string)m.Personalization!["service_name"] == "Transfer an academy"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_falls_back_to_tenant_name_when_service_name_not_configured(string? serviceName)
    {
        SetTenant(testAuthEnabled: true, serviceName: serviceName);

        await _handler.Handle(new SendTestAuthPasswordCommand(Email), CancellationToken.None);

        await _emailService.Received(1).SendEmailAsync(
            Arg.Is<EmailMessage>(m => (string)m.Personalization!["service_name"] == "TestTenant"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Placeholder_names_match_the_Notify_template_contract()
    {
        Assert.Equal("environment", SendTestAuthPasswordCommandHandler.EnvironmentPlaceholder);
        Assert.Equal("temp_password", SendTestAuthPasswordCommandHandler.TempPasswordPlaceholder);
        Assert.Equal("service_name", SendTestAuthPasswordCommandHandler.ServiceNamePlaceholder);
        Assert.Equal("TestAuthPasswordEmail", EmailTypes.TestAuthPassword);
    }

    [Fact]
    public async Task Handle_trims_email_before_issuing_and_sending()
    {
        await _handler.Handle(new SendTestAuthPasswordCommand($"  {Email} "), CancellationToken.None);

        await _passwordService.Received(1).IssueAsync(Email);
        await _emailService.Received(1).SendEmailAsync(
            Arg.Is<EmailMessage>(m => m.ToEmail == Email), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_succeeds_without_sending_when_password_still_in_resend_cooldown()
    {
        _passwordService.IssueAsync(Email).Returns((string?)null);

        var result = await _handler.Handle(new SendTestAuthPasswordCommand(Email), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await _emailService.DidNotReceiveWithAnyArgs().SendEmailAsync(default!, default);
    }

    [Fact]
    public async Task Handle_returns_forbidden_when_tenant_has_test_auth_disabled()
    {
        SetTenant(testAuthEnabled: false);

        var result = await _handler.Handle(new SendTestAuthPasswordCommand(Email), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _passwordService.DidNotReceiveWithAnyArgs().IssueAsync(default!);
        await _emailService.DidNotReceiveWithAnyArgs().SendEmailAsync(default!, default);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Prod")]
    public async Task Handle_returns_forbidden_in_production_even_when_tenant_enables_test_auth(string environment)
    {
        _hostEnvironment.EnvironmentName.Returns(environment);

        var result = await _handler.Handle(new SendTestAuthPasswordCommand(Email), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _passwordService.DidNotReceiveWithAnyArgs().IssueAsync(default!);
        await _emailService.DidNotReceiveWithAnyArgs().SendEmailAsync(default!, default);
    }

    [Fact]
    public async Task Handle_returns_forbidden_when_no_tenant_resolved()
    {
        _tenantContextAccessor.CurrentTenant.Returns((TenantConfiguration?)null);

        var result = await _handler.Handle(new SendTestAuthPasswordCommand(Email), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_throws_and_does_not_issue_password_when_template_not_configured()
    {
        _emailTemplateResolver.ResolveTenantEmailTemplateAsync(EmailTypes.TestAuthPassword).Returns((string?)null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.Handle(new SendTestAuthPasswordCommand(Email), CancellationToken.None));

        Assert.Contains(EmailTypes.TestAuthPassword, ex.Message);
        await _passwordService.DidNotReceiveWithAnyArgs().IssueAsync(default!);
        await _emailService.DidNotReceiveWithAnyArgs().SendEmailAsync(default!, default);
    }

    [Fact]
    public async Task Handle_rethrows_when_email_service_fails()
    {
        _emailService.SendEmailAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Notify down"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            _handler.Handle(new SendTestAuthPasswordCommand(Email), CancellationToken.None));
    }

    private void SetTenant(bool testAuthEnabled, string? serviceName = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["TestAuthentication:Enabled"] = testAuthEnabled.ToString()
        };
        if (serviceName is not null)
        {
            values["Layout:ServiceName"] = serviceName;
        }

        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        _tenantContextAccessor.CurrentTenant.Returns(
            new TenantConfiguration(Guid.NewGuid(), "TestTenant", settings, []));
    }
}
