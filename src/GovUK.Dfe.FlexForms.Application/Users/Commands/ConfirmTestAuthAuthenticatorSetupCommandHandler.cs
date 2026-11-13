using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Common.Attributes;
using GovUK.Dfe.FlexForms.Application.Common.Behaviours;
using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace GovUK.Dfe.FlexForms.Application.Users.Commands;

/// <summary>Returns the enrolment status: enrolled only when the code matched the pending setup.</summary>
[RateLimit(10, 60)]
public sealed record ConfirmTestAuthAuthenticatorSetupCommand(string Code)
    : IRequest<Result<TestAuthAuthenticatorStatusResponse>>, IRateLimitedRequest;

public sealed class ConfirmTestAuthAuthenticatorSetupCommandHandler(
    IHttpContextAccessor httpContextAccessor,
    ITenantContextAccessor tenantContextAccessor,
    IHostEnvironment hostEnvironment,
    ITestAuthAuthenticatorService authenticatorService)
    : IRequestHandler<ConfirmTestAuthAuthenticatorSetupCommand, Result<TestAuthAuthenticatorStatusResponse>>
{
    public async Task<Result<TestAuthAuthenticatorStatusResponse>> Handle(
        ConfirmTestAuthAuthenticatorSetupCommand request,
        CancellationToken cancellationToken)
    {
        var (email, error) = TestAuthAuthenticatorCaller.Resolve(httpContextAccessor, hostEnvironment, tenantContextAccessor);
        if (email is null)
            return Result<TestAuthAuthenticatorStatusResponse>.Forbid(error!);

        if (!await authenticatorService.ConfirmSetupAsync(email, request.Code, cancellationToken))
        {
            var existing = await authenticatorService.GetEnrolledOnAsync(email, cancellationToken);
            return Result<TestAuthAuthenticatorStatusResponse>.Success(
                new TestAuthAuthenticatorStatusResponse(false, existing));
        }

        var enrolledOn = await authenticatorService.GetEnrolledOnAsync(email, cancellationToken);
        return Result<TestAuthAuthenticatorStatusResponse>.Success(
            new TestAuthAuthenticatorStatusResponse(true, enrolledOn));
    }
}
