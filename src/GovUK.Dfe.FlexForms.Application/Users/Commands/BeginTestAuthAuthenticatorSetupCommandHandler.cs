using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Common.Attributes;
using GovUK.Dfe.FlexForms.Application.Common.Behaviours;
using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace GovUK.Dfe.FlexForms.Application.Users.Commands;

[RateLimit(10, 60)]
public sealed record BeginTestAuthAuthenticatorSetupCommand
    : IRequest<Result<BeginTestAuthAuthenticatorSetupResponse>>, IRateLimitedRequest;

public sealed class BeginTestAuthAuthenticatorSetupCommandHandler(
    IHttpContextAccessor httpContextAccessor,
    ITenantContextAccessor tenantContextAccessor,
    IHostEnvironment hostEnvironment,
    ITestAuthAuthenticatorService authenticatorService)
    : IRequestHandler<BeginTestAuthAuthenticatorSetupCommand, Result<BeginTestAuthAuthenticatorSetupResponse>>
{
    public async Task<Result<BeginTestAuthAuthenticatorSetupResponse>> Handle(
        BeginTestAuthAuthenticatorSetupCommand request,
        CancellationToken cancellationToken)
    {
        var (email, error) = TestAuthAuthenticatorCaller.Resolve(httpContextAccessor, hostEnvironment, tenantContextAccessor);
        if (email is null)
            return Result<BeginTestAuthAuthenticatorSetupResponse>.Forbid(error!);

        var issuer = TestAuthAuthenticatorCaller.BuildIssuer(tenantContextAccessor.CurrentTenant!, hostEnvironment);

        var setup = await authenticatorService.BeginSetupAsync(email, issuer, cancellationToken);
        return Result<BeginTestAuthAuthenticatorSetupResponse>.Success(
            new BeginTestAuthAuthenticatorSetupResponse(setup.SharedKey, setup.AuthenticatorUri));
    }
}
