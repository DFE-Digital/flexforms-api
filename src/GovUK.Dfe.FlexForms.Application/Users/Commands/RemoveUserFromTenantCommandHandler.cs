using System.Security.Claims;
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

namespace GovUK.Dfe.FlexForms.Application.Users.Commands;

/// <summary>
/// Removes a user from the current tenant by clearing their permissions on tenant templates
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
    ITenantTemplateCatalogue tenantTemplateCatalogue,
    ITenantContextAccessor tenantContextAccessor,
    ITenantMembershipService tenantMembershipService,
    IPermissionCheckerService permissionCheckerService,
    IHttpContextAccessor httpContextAccessor)
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

        var user = await new GetUserWithTemplatePermissionsByUserIdQueryObject(userId)
            .Apply(userRepository.Query())
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
            return Result<bool>.NotFound("User not found");

        if (!string.IsNullOrWhiteSpace(actingEmail)
            && string.Equals(user.Email, actingEmail, StringComparison.OrdinalIgnoreCase))
        {
            return Result<bool>.Failure("You cannot remove yourself from the tenant");
        }

        var catalogueIds = await tenantTemplateCatalogue.GetTemplateIdsAsync(cancellationToken);
        if (catalogueIds.Count > 0)
        {
            var catalogueSet = catalogueIds.ToHashSet();
            var tenantTemplateIds = user.TemplatePermissions
                .Where(tp => catalogueSet.Contains(tp.TemplateId))
                .Select(tp => tp.TemplateId)
                .Distinct()
                .ToList();

            if (tenantTemplateIds.Count > 0)
                userFactory.RemoveTemplatePermissionsFromUser(user, tenantTemplateIds);
        }

        await tenantMembershipService.DeactivateMembershipAsync(
            currentTenant.Id,
            userId,
            cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}
