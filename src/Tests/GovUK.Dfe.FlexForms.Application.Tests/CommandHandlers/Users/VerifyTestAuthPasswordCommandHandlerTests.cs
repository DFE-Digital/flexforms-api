using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Application.Users.Commands;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.Users;

public class VerifyTestAuthPasswordCommandHandlerTests
{
    private const string Email = "tester@education.gov.uk";
    private const string Password = "042917";

    private readonly ITenantContextAccessor _tenantContextAccessor = Substitute.For<ITenantContextAccessor>();
    private readonly IHostEnvironment _hostEnvironment = Substitute.For<IHostEnvironment>();
    private readonly ITestAuthPasswordService _passwordService = Substitute.For<ITestAuthPasswordService>();
    private readonly VerifyTestAuthPasswordCommandHandler _handler;

    public VerifyTestAuthPasswordCommandHandlerTests()
    {
        _hostEnvironment.EnvironmentName.Returns("Development");
        SetTenant(testAuthEnabled: true);

        _handler = new VerifyTestAuthPasswordCommandHandler(
            NullLogger<VerifyTestAuthPasswordCommandHandler>.Instance,
            _tenantContextAccessor,
            _hostEnvironment,
            _passwordService);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_returns_verification_outcome_from_password_service(bool isValid)
    {
        _passwordService.VerifyAsync(Email, Password).Returns(isValid);

        var result = await _handler.Handle(new VerifyTestAuthPasswordCommand(Email, Password), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(isValid, result.Value!.IsValid);
    }

    [Fact]
    public async Task Handle_trims_email_before_verifying()
    {
        _passwordService.VerifyAsync(Email, Password).Returns(true);

        var result = await _handler.Handle(
            new VerifyTestAuthPasswordCommand($" {Email}  ", Password),
            CancellationToken.None);

        Assert.True(result.Value!.IsValid);
        await _passwordService.Received(1).VerifyAsync(Email, Password);
    }

    [Fact]
    public async Task Handle_returns_forbidden_when_tenant_has_test_auth_disabled()
    {
        SetTenant(testAuthEnabled: false);

        var result = await _handler.Handle(new VerifyTestAuthPasswordCommand(Email, Password), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _passwordService.DidNotReceiveWithAnyArgs().VerifyAsync(default!, default!);
    }

    [Fact]
    public async Task Handle_returns_forbidden_in_production()
    {
        _hostEnvironment.EnvironmentName.Returns(Environments.Production);

        var result = await _handler.Handle(new VerifyTestAuthPasswordCommand(Email, Password), CancellationToken.None);

        Assert.Equal(DomainErrorCode.Forbidden, result.ErrorCode);
        await _passwordService.DidNotReceiveWithAnyArgs().VerifyAsync(default!, default!);
    }

    private void SetTenant(bool testAuthEnabled)
    {
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TestAuthentication:Enabled"] = testAuthEnabled.ToString()
            })
            .Build();

        _tenantContextAccessor.CurrentTenant.Returns(
            new TenantConfiguration(Guid.NewGuid(), "TestTenant", settings, []));
    }
}
