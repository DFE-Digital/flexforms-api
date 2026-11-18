using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Common.Attributes;
using GovUK.Dfe.FlexForms.Application.Common.Behaviours;
using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Domain.Services;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.Users.Commands;

/// <summary>
/// An administrator locks a tenant user out of every Test Authentication sign-in method until they unlock them, or
/// unlocks them. Unlocking also ends any temporary lockout from wrong codes. Every change is written to the tenant
/// access audit.
/// </summary>
[RateLimit(10, 60)]
public sealed record SetTestAuthSignInLockCommand(Guid UserId, bool IsLocked)
    : IRequest<Result<TestAuthSignInLockStatusResponse>>, IRateLimitedRequest;

public sealed class SetTestAuthSignInLockCommandHandler(
    TestAuthSignInLockAdminGuard guard,
    ITestAuthSignInLockService signInLockService,
    ITenantAccessAuditWriter accessAuditWriter)
    : IRequestHandler<SetTestAuthSignInLockCommand, Result<TestAuthSignInLockStatusResponse>>
{
    public const string LockedAuditAction = "TestAuthSignInLocked";
    public const string UnlockedAuditAction = "TestAuthSignInUnlocked";

    public async Task<Result<TestAuthSignInLockStatusResponse>> Handle(
        SetTestAuthSignInLockCommand request,
        CancellationToken cancellationToken)
    {
        var (target, failure) = await guard.ResolveAsync(request.UserId, cancellationToken);
        if (target is null)
            return failure!;

        if (target.RequiresSuperAdmin)
            return Result<TestAuthSignInLockStatusResponse>.Forbid(TestAuthSignInLockAdminGuard.AdministratorTargetMessage);

        if (request.IsLocked && target.IsSelf)
            return Result<TestAuthSignInLockStatusResponse>.Failure(TestAuthSignInLockAdminGuard.SelfLockMessage);

        var changed = request.IsLocked
            ? await signInLockService.LockAsync(target.Email, target.ActorEmail, cancellationToken)
            : await signInLockService.UnlockAsync(target.Email, cancellationToken);

        if (changed)
        {
            await accessAuditWriter.AppendAsync(
                target.TenantId,
                target.UserId,
                target.Email,
                request.IsLocked ? LockedAuditAction : UnlockedAuditAction,
                roleName: null,
                target.ActorUserId,
                target.ActorEmail,
                request.IsLocked
                    ? "Locked out of Test Authentication sign-in until an administrator unlocks them"
                    : "Test Authentication sign-in unlocked",
                cancellationToken);
        }

        var status = await signInLockService.GetStatusAsync(target.Email, cancellationToken);
        return Result<TestAuthSignInLockStatusResponse>.Success(TestAuthSignInLockAdminGuard.ToResponse(status));
    }
}
