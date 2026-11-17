using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace GovUK.Dfe.FlexForms.Application.Users;

/// <summary>
/// The checks every administrator action on Test Authentication sign-in locks shares: Test Authentication is on for
/// the tenant, the caller is a person who can manage users, the target is a member of the tenant, and only a
/// SuperAdmin may act on another administrator. Same rules as authenticator setup codes.
/// </summary>
public sealed class TestAuthSignInLockAdminGuard(
    IHttpContextAccessor httpContextAccessor,
    ITenantContextAccessor tenantContextAccessor,
    IHostEnvironment hostEnvironment,
    IPermissionCheckerService permissionCheckerService,
    IEaRepository<User> userRepository,
    ITenantMembershipService tenantMembershipService)
{
    public const string NotAllowedMessage = "Only signed-in administrators can manage sign-in locks.";
    public const string UserNotFoundMessage = "User not found";
    public const string AdministratorTargetMessage =
        "Only a SuperAdmin can lock or unlock another administrator's sign-in.";
    public const string SelfLockMessage = "You cannot lock yourself out of sign-in.";

    /// <summary>
    /// Resolves the target user and the acting administrator, or the failure to return.
    /// </summary>
    public async Task<(TestAuthSignInLockTarget? Target, Result<TestAuthSignInLockStatusResponse>? Failure)> ResolveAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var tenant = tenantContextAccessor.CurrentTenant;
        if (!TestAuthenticationEnvironmentGate.IsEnabledForTenant(hostEnvironment, tenant))
            return (null, Result<TestAuthSignInLockStatusResponse>.Forbid(TestAuthAuthenticatorCaller.NotEnabledMessage));

        // Only a person may lock or unlock users: a service principal with admin role claims must not.
        var actorEmail = PermissionClaimEvaluator.GetInteractiveUserEmail(httpContextAccessor.HttpContext?.User);
        if (actorEmail is null || !permissionCheckerService.CanManageUsers())
            return (null, Result<TestAuthSignInLockStatusResponse>.Forbid(NotAllowedMessage));

        var id = new UserId(userId);
        var subject = await userRepository.Query()
            .AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (subject is null || string.IsNullOrWhiteSpace(subject.Email))
            return (null, Result<TestAuthSignInLockStatusResponse>.NotFound(UserNotFoundMessage));

        var isPlatformSuperAdmin = RoleNames.IsPlatformSuperAdminUser(subject.Role?.Name, subject.RoleId.Value);
        var membership = await tenantMembershipService.GetActiveMembershipAsync(tenant!.Id, subject.Id!, cancellationToken);
        if (membership is null && !isPlatformSuperAdmin)
            return (null, Result<TestAuthSignInLockStatusResponse>.NotFound(UserNotFoundMessage));

        var isAdministrator = isPlatformSuperAdmin
            || RoleNames.IsSuperAdmin(membership?.Role?.Name)
            || string.Equals(membership?.Role?.Name, RoleNames.Admin, StringComparison.OrdinalIgnoreCase);

        var isSelf = string.Equals(actorEmail, subject.Email.Trim(), StringComparison.OrdinalIgnoreCase);

        var actor = await new GetUserByEmailQueryObject(actorEmail)
            .Apply(userRepository.Query().AsNoTracking())
            .FirstOrDefaultAsync(cancellationToken);

        return (new TestAuthSignInLockTarget(
            tenant.Id,
            subject.Id!,
            subject.Email.Trim(),
            isSelf,
            isAdministrator && !isSelf && !permissionCheckerService.IsInteractivePlatformAdmin(),
            actor?.Id,
            actorEmail), null);
    }

    public static TestAuthSignInLockStatusResponse ToResponse(TestAuthSignInLockStatus status)
        => new(
            status.IsManuallyLocked,
            status.LockedAtUtc,
            status.LockedByEmail,
            status.TemporaryLockoutRemaining is not null,
            TestAuthSignInResult.ToWholeSeconds(status.TemporaryLockoutRemaining));
}

/// <param name="RequiresSuperAdmin">
/// <c>true</c> when the target is another administrator and the caller is not a SuperAdmin, so changes are refused.
/// </param>
public sealed record TestAuthSignInLockTarget(
    Guid TenantId,
    UserId UserId,
    string Email,
    bool IsSelf,
    bool RequiresSuperAdmin,
    UserId? ActorUserId,
    string ActorEmail);
