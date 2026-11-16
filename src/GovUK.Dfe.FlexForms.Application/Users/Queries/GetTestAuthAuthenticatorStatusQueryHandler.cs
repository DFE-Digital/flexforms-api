using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace GovUK.Dfe.FlexForms.Application.Users.Queries;

public sealed record GetTestAuthAuthenticatorStatusQuery : IRequest<Result<TestAuthAuthenticatorStatusResponse>>;

public sealed class GetTestAuthAuthenticatorStatusQueryHandler(
    IHttpContextAccessor httpContextAccessor,
    ITenantContextAccessor tenantContextAccessor,
    IHostEnvironment hostEnvironment,
    ITestAuthAuthenticatorService authenticatorService)
    : IRequestHandler<GetTestAuthAuthenticatorStatusQuery, Result<TestAuthAuthenticatorStatusResponse>>
{
    public async Task<Result<TestAuthAuthenticatorStatusResponse>> Handle(
        GetTestAuthAuthenticatorStatusQuery request,
        CancellationToken cancellationToken)
    {
        var (email, error) = TestAuthAuthenticatorCaller.Resolve(httpContextAccessor, hostEnvironment, tenantContextAccessor);
        if (email is null)
            return Result<TestAuthAuthenticatorStatusResponse>.Forbid(error!);

        var enrolledOn = await authenticatorService.GetEnrolledOnAsync(email, cancellationToken);
        return Result<TestAuthAuthenticatorStatusResponse>.Success(
            new TestAuthAuthenticatorStatusResponse(enrolledOn.HasValue, enrolledOn));
    }
}
