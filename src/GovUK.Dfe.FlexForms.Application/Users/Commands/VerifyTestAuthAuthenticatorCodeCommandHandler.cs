using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.Users.Commands;

public sealed record VerifyTestAuthAuthenticatorCodeCommand(string Email, string Code)
    : IRequest<Result<VerifyTestAuthPasswordResponse>>;

public sealed class VerifyTestAuthAuthenticatorCodeCommandHandler(
    ILogger<VerifyTestAuthAuthenticatorCodeCommandHandler> logger,
    ITenantContextAccessor tenantContextAccessor,
    IHostEnvironment hostEnvironment,
    ITestAuthAuthenticatorService authenticatorService,
    ITestAuthSignInLockService signInLockService)
    : IRequestHandler<VerifyTestAuthAuthenticatorCodeCommand, Result<VerifyTestAuthPasswordResponse>>
{
    public async Task<Result<VerifyTestAuthPasswordResponse>> Handle(
        VerifyTestAuthAuthenticatorCodeCommand request,
        CancellationToken cancellationToken)
    {
        if (!TestAuthenticationEnvironmentGate.IsEnabledForTenant(hostEnvironment, tenantContextAccessor.CurrentTenant))
        {
            logger.LogWarning(
                "Test authentication authenticator sign-in attempted for {Email} but Test Authentication is not enabled in {Environment}",
                request.Email,
                hostEnvironment.EnvironmentName);
            return Result<VerifyTestAuthPasswordResponse>.Forbid(TestAuthAuthenticatorCaller.NotEnabledMessage);
        }

        var email = request.Email.Trim();

        // Checked before the attempt is counted: a locked user is refused whatever they enter.
        if (await signInLockService.IsManuallyLockedAsync(email, cancellationToken))
        {
            logger.LogWarning("Test authentication authenticator sign-in refused for {Email}: locked by an administrator", email);
            return Result<VerifyTestAuthPasswordResponse>.Success(TestAuthSignInResult.ManuallyLocked.ToResponse());
        }

        var result = await authenticatorService.VerifyAsync(email, request.Code, cancellationToken);

        if (result.IsValid)
            logger.LogInformation("Test authentication authenticator code verified for {Email}", request.Email);
        else
            logger.LogWarning("Invalid test authentication authenticator code entered for {Email}", request.Email);

        return Result<VerifyTestAuthPasswordResponse>.Success(result.ToResponse());
    }
}
