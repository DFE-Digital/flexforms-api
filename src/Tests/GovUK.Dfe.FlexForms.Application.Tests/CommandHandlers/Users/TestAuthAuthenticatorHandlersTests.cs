using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Application.Users.Commands;
using GovUK.Dfe.FlexForms.Application.Users.Queries;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.Users;

public class TestAuthAuthenticatorHandlersTests
{
    private const string Email = "tester@education.gov.uk";
    private static readonly DateTime EnrolledOn = new(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc);

    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
    private readonly ITenantContextAccessor _tenantContextAccessor = Substitute.For<ITenantContextAccessor>();
    private readonly IHostEnvironment _hostEnvironment = Substitute.For<IHostEnvironment>();
    private readonly ITestAuthAuthenticatorService _service = Substitute.For<ITestAuthAuthenticatorService>();

    public TestAuthAuthenticatorHandlersTests()
    {
        _hostEnvironment.EnvironmentName.Returns("Development");
        SetTenant(testAuthEnabled: true);
        SetUser(new Claim(ClaimTypes.Email, Email));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Verify_returns_outcome_from_service(bool isValid)
    {
        _service.VerifyAsync(Email, "123456", Arg.Any<CancellationToken>()).Returns(isValid);

        var result = await VerifyHandler().Handle(
            new VerifyTestAuthAuthenticatorCodeCommand($" {Email} ", "123456"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(isValid, result.Value!.IsValid);
    }

    [Fact]
    public async Task Verify_is_forbidden_when_tenant_has_test_auth_disabled()
    {
        SetTenant(testAuthEnabled: false);

        var result = await VerifyHandler().Handle(
            new VerifyTestAuthAuthenticatorCodeCommand(Email, "123456"),
            CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _service.DidNotReceiveWithAnyArgs().VerifyAsync(default!, default!, default);
    }

    [Fact]
    public async Task Verify_is_forbidden_in_production()
    {
        _hostEnvironment.EnvironmentName.Returns(Environments.Production);

        var result = await VerifyHandler().Handle(
            new VerifyTestAuthAuthenticatorCodeCommand(Email, "123456"),
            CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _service.DidNotReceiveWithAnyArgs().VerifyAsync(default!, default!, default);
    }

    [Fact]
    public async Task BeginSetup_uses_email_from_token_and_service_name_with_environment_as_issuer()
    {
        _service.BeginSetupAsync(Email, "Transfers (Development)", Arg.Any<CancellationToken>())
            .Returns(new TestAuthAuthenticatorSetup("KEY", "otpauth://totp/x"));

        var result = await BeginHandler().Handle(new BeginTestAuthAuthenticatorSetupCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("KEY", result.Value!.SharedKey);
        Assert.Equal("otpauth://totp/x", result.Value.AuthenticatorUri);
    }

    [Fact]
    public async Task BeginSetup_is_forbidden_when_tenant_has_test_auth_disabled()
    {
        SetTenant(testAuthEnabled: false);

        var result = await BeginHandler().Handle(new BeginTestAuthAuthenticatorSetupCommand(), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        Assert.Equal(TestAuthAuthenticatorCaller.NotEnabledMessage, result.Error);
        await _service.DidNotReceiveWithAnyArgs().BeginSetupAsync(default!, default!, default);
    }

    [Fact]
    public async Task BeginSetup_requires_an_authenticated_user()
    {
        _httpContextAccessor.HttpContext.Returns(new DefaultHttpContext());

        var result = await BeginHandler().Handle(new BeginTestAuthAuthenticatorSetupCommand(), CancellationToken.None);

        Assert.Equal("Not authenticated", result.Error);
    }

    [Fact]
    public async Task BeginSetup_rejects_callers_without_an_email()
    {
        SetUser(new Claim("appid", "service-principal"));

        var result = await BeginHandler().Handle(new BeginTestAuthAuthenticatorSetupCommand(), CancellationToken.None);

        Assert.Equal("No user identifier", result.Error);
        await _service.DidNotReceiveWithAnyArgs().BeginSetupAsync(default!, default!, default);
    }

    [Fact]
    public async Task ConfirmSetup_returns_enrolled_status_when_code_matches()
    {
        _service.ConfirmSetupAsync(Email, "123456", Arg.Any<CancellationToken>()).Returns(true);
        _service.GetEnrolledOnAsync(Email, Arg.Any<CancellationToken>()).Returns(EnrolledOn);

        var result = await ConfirmHandler().Handle(new ConfirmTestAuthAuthenticatorSetupCommand("123456"), CancellationToken.None);

        Assert.True(result.Value!.IsEnrolled);
        Assert.Equal(EnrolledOn, result.Value.EnrolledOnUtc);
    }

    [Fact]
    public async Task ConfirmSetup_returns_not_enrolled_with_existing_enrolment_date_when_code_is_wrong()
    {
        _service.ConfirmSetupAsync(Email, "123456", Arg.Any<CancellationToken>()).Returns(false);
        _service.GetEnrolledOnAsync(Email, Arg.Any<CancellationToken>()).Returns(EnrolledOn);

        var result = await ConfirmHandler().Handle(new ConfirmTestAuthAuthenticatorSetupCommand("123456"), CancellationToken.None);

        Assert.False(result.Value!.IsEnrolled);
        Assert.Equal(EnrolledOn, result.Value.EnrolledOnUtc);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Status_reports_whether_enrolled(bool enrolled)
    {
        _service.GetEnrolledOnAsync(Email, Arg.Any<CancellationToken>()).Returns(enrolled ? EnrolledOn : null);

        var result = await StatusHandler().Handle(new GetTestAuthAuthenticatorStatusQuery(), CancellationToken.None);

        Assert.Equal(enrolled, result.Value!.IsEnrolled);
        Assert.Equal(enrolled ? EnrolledOn : null, result.Value.EnrolledOnUtc);
    }

    [Fact]
    public async Task Status_is_forbidden_in_production()
    {
        _hostEnvironment.EnvironmentName.Returns(Environments.Production);

        var result = await StatusHandler().Handle(new GetTestAuthAuthenticatorStatusQuery(), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
    }

    [Fact]
    public async Task Remove_removes_the_callers_enrolment()
    {
        _service.RemoveAsync(Email, Arg.Any<CancellationToken>()).Returns(true);

        var result = await RemoveHandler().Handle(new RemoveTestAuthAuthenticatorCommand(), CancellationToken.None);

        Assert.True(result.Value);
        await _service.Received(1).RemoveAsync(Email, Arg.Any<CancellationToken>());
    }

    private VerifyTestAuthAuthenticatorCodeCommandHandler VerifyHandler()
        => new(NullLogger<VerifyTestAuthAuthenticatorCodeCommandHandler>.Instance, _tenantContextAccessor, _hostEnvironment, _service);

    private BeginTestAuthAuthenticatorSetupCommandHandler BeginHandler()
        => new(_httpContextAccessor, _tenantContextAccessor, _hostEnvironment, _service);

    private ConfirmTestAuthAuthenticatorSetupCommandHandler ConfirmHandler()
        => new(_httpContextAccessor, _tenantContextAccessor, _hostEnvironment, _service);

    private GetTestAuthAuthenticatorStatusQueryHandler StatusHandler()
        => new(_httpContextAccessor, _tenantContextAccessor, _hostEnvironment, _service);

    private RemoveTestAuthAuthenticatorCommandHandler RemoveHandler()
        => new(_httpContextAccessor, _tenantContextAccessor, _hostEnvironment, _service);

    private void SetUser(params Claim[] claims)
        => _httpContextAccessor.HttpContext.Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TenantBearer"))
        });

    private void SetTenant(bool testAuthEnabled)
    {
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TestAuthentication:Enabled"] = testAuthEnabled.ToString(),
                ["Layout:ServiceName"] = "Transfers"
            })
            .Build();

        _tenantContextAccessor.CurrentTenant.Returns(
            new TenantConfiguration(Guid.NewGuid(), "TestTenant", settings, []));
    }
}
