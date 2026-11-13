using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace GovUK.Dfe.FlexForms.Application.Users.Commands;

/// <summary>Removes the signed-in user's authenticator. Returns <c>true</c> when one was enrolled.</summary>
public sealed record RemoveTestAuthAuthenticatorCommand : IRequest<Result<bool>>;

public sealed class RemoveTestAuthAuthenticatorCommandHandler(
    IHttpContextAccessor httpContextAccessor,
    ITenantContextAccessor tenantContextAccessor,
    IHostEnvironment hostEnvironment,
    ITestAuthAuthenticatorService authenticatorService)
    : IRequestHandler<RemoveTestAuthAuthenticatorCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(RemoveTestAuthAuthenticatorCommand request, CancellationToken cancellationToken)
    {
        var (email, error) = TestAuthAuthenticatorCaller.Resolve(httpContextAccessor, hostEnvironment, tenantContextAccessor);
        if (email is null)
            return Result<bool>.Forbid(error!);

        return Result<bool>.Success(await authenticatorService.RemoveAsync(email, cancellationToken));
    }
}
