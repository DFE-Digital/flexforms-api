using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.Users.Commands;

public sealed record VerifyTestAuthPasswordCommand(string Email, string Password)
    : IRequest<Result<VerifyTestAuthPasswordResponse>>;

public sealed class VerifyTestAuthPasswordCommandHandler(
    ILogger<VerifyTestAuthPasswordCommandHandler> logger,
    ITenantContextAccessor tenantContextAccessor,
    IHostEnvironment hostEnvironment,
    ITestAuthPasswordService testAuthPasswordService)
    : IRequestHandler<VerifyTestAuthPasswordCommand, Result<VerifyTestAuthPasswordResponse>>
{
    public async Task<Result<VerifyTestAuthPasswordResponse>> Handle(
        VerifyTestAuthPasswordCommand request,
        CancellationToken cancellationToken)
    {
        if (!TestAuthenticationEnvironmentGate.IsEnabledForTenant(hostEnvironment, tenantContextAccessor.CurrentTenant))
        {
            logger.LogWarning(
                "Test authentication password verification attempted for {Email} but Test Authentication is not enabled in {Environment}",
                request.Email,
                hostEnvironment.EnvironmentName);
            return Result<VerifyTestAuthPasswordResponse>.Forbid("Test authentication is not enabled for this tenant.");
        }

        var isValid = await testAuthPasswordService.VerifyAsync(request.Email.Trim(), request.Password);

        if (isValid)
        {
            logger.LogInformation("Test authentication password verified for {Email}", request.Email);
        }
        else
        {
            logger.LogWarning("Invalid or expired test authentication password entered for {Email}", request.Email);
        }

        return Result<VerifyTestAuthPasswordResponse>.Success(new VerifyTestAuthPasswordResponse(isValid));
    }
}
