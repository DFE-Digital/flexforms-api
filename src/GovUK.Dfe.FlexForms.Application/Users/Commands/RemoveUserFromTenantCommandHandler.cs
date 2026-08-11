using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Application.Users.Commands;

/// <summary>
/// Removes a user from the current tenant by clearing their permissions on tenant resources
/// and deactivating their tenant membership. The user account row is left intact.
/// </summary>
public sealed record RemoveUserFromTenantCommand(Guid UserId)
    : IRequest<Result<bool>>;

/// <summary>
/// Handles <see cref="RemoveUserFromTenantCommand"/>.
/// </summary>
public sealed class RemoveUserFromTenantCommandHandler(
    IEaRepository<User> userRepository,
    IUnitOfWork unitOfWork,
    IUserFactory userFactory,
    ITenantPermissionFilter tenantPermissionFilter,
    ITenantContextAccessor tenantContextAccessor,
    ITenantMembershipService tenantMembershipService,
    IPermissionCheckerService permissionCheckerService,
    IHttpContextAccessor httpContextAccessor,
    ITenantAccessAuditWriter accessAuditWriter,
    IUserCacheInvalidator userCacheInvalidator)
    : IRequestHandler<RemoveUserFromTenantCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        RemoveUserFromTenantCommand command,
        CancellationToken cancellationToken)
    {
        if (!permissionCheckerService.CanManageUsers())
            return Result<bool>.Forbid("Only administrators can remove users from the tenant");

        var currentTenant = tenantContextAccessor.CurrentTenant;
        if (currentTenant is null)
            return Result<bool>.Forbid("Tenant context is required");

        var actingEmail = httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Email);
        var userId = new UserId(command.UserId);

        var user = await new GetUserWithAllPermissionsByUserIdQueryObject(userId)
            .Apply(userRepository.Query())
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
            return Result<bool>.NotFound("User not found");

        if (!string.IsNullOrWhiteSpace(actingEmail)
            && string.Equals(user.Email, actingEmail, StringComparison.OrdinalIgnoreCase))
        {
            return Result<bool>.Failure("You cannot remove yourself from the tenant");
        }

        var tenantPermissions = await tenantPermissionFilter.FilterToCurrentTenantAsync(
            user.Permissions,
            cancellationToken);

        foreach (var permission in tenantPermissions)
            userFactory.RemovePermissionFromUser(user, permission);

        await tenantMembershipService.DeactivateMembershipAsync(
            currentTenant.Id,
            userId,
            cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);

        await userCacheInvalidator.InvalidateForUserAsync(
            user.Email,
            user.ExternalProviderId,
            userId,
            cancellationToken);

        var actorEmail = actingEmail
            ?? httpContextAccessor.HttpContext?.User?.Identity?.Name
            ?? "unknown";

        UserId? actorUserId = null;
        if (!string.IsNullOrWhiteSpace(actingEmail))
        {
            var actor = await new GetUserByEmailQueryObject(actingEmail)
                .Apply(userRepository.Query().AsNoTracking())
                .FirstOrDefaultAsync(cancellationToken);
            actorUserId = actor?.Id;
        }

        await accessAuditWriter.AppendAsync(
            currentTenant.Id,
            userId,
            user.Email,
            "MembershipDeactivated",
            roleName: null,
            actorUserId,
            actorEmail,
            "User removed from tenant",
            cancellationToken);

        return Result<bool>.Success(true);
    }
}
