using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.Users.Commands;

/// <summary>
/// Consumes an administrator-issued setup code and starts authenticator app enrolment for the email it was issued
/// for. The email comes from the code, so a valid code is the only way to start an anonymous enrolment.
/// </summary>
public sealed record RedeemTestAuthAuthenticatorInviteCommand(string Email, string InviteCode)
    : IRequest<Result<BeginTestAuthAuthenticatorSetupResponse>>;

public sealed class RedeemTestAuthAuthenticatorInviteCommandHandler(
    ILogger<RedeemTestAuthAuthenticatorInviteCommandHandler> logger,
    ITenantContextAccessor tenantContextAccessor,
    IHostEnvironment hostEnvironment,
    ITestAuthAuthenticatorInviteService inviteService,
    ITestAuthAuthenticatorService authenticatorService)
    : IRequestHandler<RedeemTestAuthAuthenticatorInviteCommand, Result<BeginTestAuthAuthenticatorSetupResponse>>
{
    public const string InvalidCodeMessage = "The setup code is incorrect, has expired or has already been used.";

    public async Task<Result<BeginTestAuthAuthenticatorSetupResponse>> Handle(
        RedeemTestAuthAuthenticatorInviteCommand request,
        CancellationToken cancellationToken)
    {
        var tenant = tenantContextAccessor.CurrentTenant;
        if (!TestAuthenticationEnvironmentGate.IsEnabledForTenant(hostEnvironment, tenant))
            return Result<BeginTestAuthAuthenticatorSetupResponse>.Forbid(TestAuthAuthenticatorCaller.NotEnabledMessage);

        var email = request.Email.Trim();
        if (!await inviteService.RedeemAsync(email, request.InviteCode))
        {
            logger.LogWarning("Invalid test authentication authenticator setup code entered for {Email}", email);
            return Result<BeginTestAuthAuthenticatorSetupResponse>.Failure(InvalidCodeMessage);
        }

        var setup = await authenticatorService.BeginInviteSetupAsync(
            email,
            TestAuthAuthenticatorCaller.BuildIssuer(tenant!, hostEnvironment),
            cancellationToken);

        logger.LogInformation("Test authentication authenticator setup code used for {Email}", email);
        return Result<BeginTestAuthAuthenticatorSetupResponse>.Success(
            new BeginTestAuthAuthenticatorSetupResponse(setup.SharedKey, setup.AuthenticatorUri));
    }
}
