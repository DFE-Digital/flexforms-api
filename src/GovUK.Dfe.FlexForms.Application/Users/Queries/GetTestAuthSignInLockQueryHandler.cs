using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Security;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.Users.Queries;

/// <summary>Whether a tenant user is locked out of Test Authentication sign-in, for User Manager.</summary>
public sealed record GetTestAuthSignInLockQuery(Guid UserId) : IRequest<Result<TestAuthSignInLockStatusResponse>>;

public sealed class GetTestAuthSignInLockQueryHandler(
    TestAuthSignInLockAdminGuard guard,
    ITestAuthSignInLockService signInLockService)
    : IRequestHandler<GetTestAuthSignInLockQuery, Result<TestAuthSignInLockStatusResponse>>
{
    public async Task<Result<TestAuthSignInLockStatusResponse>> Handle(
        GetTestAuthSignInLockQuery request,
        CancellationToken cancellationToken)
    {
        var (target, failure) = await guard.ResolveAsync(request.UserId, cancellationToken);
        if (target is null)
            return failure!;

        var status = await signInLockService.GetStatusAsync(target.Email, cancellationToken);
        return Result<TestAuthSignInLockStatusResponse>.Success(TestAuthSignInLockAdminGuard.ToResponse(status));
    }
}
