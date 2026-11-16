using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.Users.Commands;

/// <summary>
/// Completes an enrolment started from a setup code. Only pending enrolments created by
/// <see cref="RedeemTestAuthAuthenticatorInviteCommand"/> can be completed here. A valid code proves the user holds the
/// new app, so Web may sign them in straight away.
/// </summary>
public sealed record ConfirmTestAuthAuthenticatorInviteSetupCommand(string Email, string Code)
    : IRequest<Result<VerifyTestAuthPasswordResponse>>;

public sealed class ConfirmTestAuthAuthenticatorInviteSetupCommandHandler(
    ILogger<ConfirmTestAuthAuthenticatorInviteSetupCommandHandler> logger,
    ITenantContextAccessor tenantContextAccessor,
    IHostEnvironment hostEnvironment,
    ITestAuthAuthenticatorService authenticatorService)
    : IRequestHandler<ConfirmTestAuthAuthenticatorInviteSetupCommand, Result<VerifyTestAuthPasswordResponse>>
{
    public async Task<Result<VerifyTestAuthPasswordResponse>> Handle(
        ConfirmTestAuthAuthenticatorInviteSetupCommand request,
        CancellationToken cancellationToken)
    {
        if (!TestAuthenticationEnvironmentGate.IsEnabledForTenant(hostEnvironment, tenantContextAccessor.CurrentTenant))
            return Result<VerifyTestAuthPasswordResponse>.Forbid(TestAuthAuthenticatorCaller.NotEnabledMessage);

        var email = request.Email.Trim();
        var enrolled = await authenticatorService.ConfirmInviteSetupAsync(email, request.Code, cancellationToken);

        if (!enrolled)
            logger.LogWarning("Wrong code entered while setting up an authenticator from a setup code for {Email}", email);

        return Result<VerifyTestAuthPasswordResponse>.Success(new VerifyTestAuthPasswordResponse(enrolled));
    }
}
