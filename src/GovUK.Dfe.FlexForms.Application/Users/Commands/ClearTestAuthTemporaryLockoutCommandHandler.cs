using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Common.Attributes;
using GovUK.Dfe.FlexForms.Application.Common.Behaviours;
using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Domain.Services;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.Users.Commands;

/// <summary>
/// An administrator ends a tenant user's temporary authenticator app lockout (too many wrong codes) before it expires.
/// A manual lock is left in place. Written to the tenant access audit.
/// </summary>
[RateLimit(10, 60)]
public sealed record ClearTestAuthTemporaryLockoutCommand(Guid UserId)
    : IRequest<Result<TestAuthSignInLockStatusResponse>>, IRateLimitedRequest;

public sealed class ClearTestAuthTemporaryLockoutCommandHandler(
    TestAuthSignInLockAdminGuard guard,
    ITestAuthSignInLockService signInLockService,
    ITenantAccessAuditWriter accessAuditWriter)
    : IRequestHandler<ClearTestAuthTemporaryLockoutCommand, Result<TestAuthSignInLockStatusResponse>>
{
    public const string AuditAction = "TestAuthTemporaryLockoutCleared";

    public async Task<Result<TestAuthSignInLockStatusResponse>> Handle(
        ClearTestAuthTemporaryLockoutCommand request,
        CancellationToken cancellationToken)
    {
        var (target, failure) = await guard.ResolveAsync(request.UserId, cancellationToken);
        if (target is null)
            return failure!;

        if (target.RequiresSuperAdmin)
            return Result<TestAuthSignInLockStatusResponse>.Forbid(TestAuthSignInLockAdminGuard.AdministratorTargetMessage);

        var before = await signInLockService.GetStatusAsync(target.Email, cancellationToken);
        if (before.TemporaryLockoutRemaining is not null)
        {
            await signInLockService.ClearTemporaryLockoutAsync(target.Email, cancellationToken);
            await accessAuditWriter.AppendAsync(
                target.TenantId,
                target.UserId,
                target.Email,
                AuditAction,
                roleName: null,
                target.ActorUserId,
                target.ActorEmail,
                "Temporary authenticator app lockout cleared",
                cancellationToken);
        }

        var status = await signInLockService.GetStatusAsync(target.Email, cancellationToken);
        return Result<TestAuthSignInLockStatusResponse>.Success(TestAuthSignInLockAdminGuard.ToResponse(status));
    }
}
