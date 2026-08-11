using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
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
/// Removes a user from the current tenant by deactivating membership and clearing
/// template (form) access. Application permissions are retained so that re-adding
/// the user restores access to their existing applications.
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
    ITenantTemplateCatalogue tenantTemplateCatalogue,
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

        // Revoke form access for this tenant only. Keep Application / ApplicationFiles
        // grants so a later re-add restores dashboard access without manual re-grants.
        var catalogueIds = await tenantTemplateCatalogue.GetTemplateIdsAsync(cancellationToken);
        if (catalogueIds.Count > 0)
        {
            var catalogueSet = catalogueIds.ToHashSet();
            var tenantTemplateIds = UserTemplateAccess.GetTemplateIds(user)
                .Where(catalogueSet.Contains)
                .ToList();

            if (tenantTemplateIds.Count > 0)
                userFactory.RemoveTemplatePermissionsFromUser(user, tenantTemplateIds);
        }

        // Also remove Template "Any" grants — those are tenant-wide create/access
        // capabilities, not application case access.
        foreach (var anyTemplateGrant in user.Permissions
                     .Where(p => p.ResourceType == ResourceType.Template
                                 && string.Equals(
                                     p.ResourceKey,
                                     Domain.Common.PermissionConstants.AnyResourceKey,
                                     StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            userFactory.RemovePermissionFromUser(user, anyTemplateGrant);
        }

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
