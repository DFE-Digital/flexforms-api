using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Users.Commands;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Users.Queries;

/// <summary>
/// Returns direct (user-owned) permission grants for a tenant member,
/// filtered to only include permissions relevant to the current tenant.
/// Does not include permissions inherited from the user's role.
/// </summary>
public sealed record GetUserPermissionsQuery(Guid UserId)
    : IRequest<Result<IReadOnlyCollection<UserPermissionDto>>>;

public sealed class GetUserPermissionsQueryHandler(
    IPermissionCheckerService permissionCheckerService,
    ITenantContextAccessor tenantContextAccessor,
    ITenantMembershipService tenantMembershipService,
    ITenantTemplateCatalogue tenantTemplateCatalogue,
    IEaRepository<User> userRepository)
    : IRequestHandler<GetUserPermissionsQuery, Result<IReadOnlyCollection<UserPermissionDto>>>
{
    public async Task<Result<IReadOnlyCollection<UserPermissionDto>>> Handle(
        GetUserPermissionsQuery request,
        CancellationToken cancellationToken)
    {
        if (!permissionCheckerService.CanManageUsers())
            return Result<IReadOnlyCollection<UserPermissionDto>>.Forbid("Only administrators can view user permissions");

        var tenant = tenantContextAccessor.CurrentTenant;
        if (tenant is null)
            return Result<IReadOnlyCollection<UserPermissionDto>>.Forbid("Tenant context is required");

        var userId = new UserId(request.UserId);
        var user = await new GetUserWithAllPermissionsByUserIdQueryObject(userId)
            .Apply(userRepository.Query().AsNoTracking())
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
            return Result<IReadOnlyCollection<UserPermissionDto>>.NotFound("User not found");

        var membership = await tenantMembershipService.GetActiveMembershipAsync(
            tenant.Id,
            userId,
            cancellationToken);

        if (membership is null)
            return Result<IReadOnlyCollection<UserPermissionDto>>.NotFound("User is not an active member of this tenant");

        var tenantTemplateIds = (await tenantTemplateCatalogue.GetTemplateIdsAsync(cancellationToken))
            .ToHashSet();

        var tenantPermissions = user.Permissions
            .Where(p => BelongsToTenant(p, tenantTemplateIds))
            .Select(SetUserPermissionsCommandHandler.Map)
            .ToList();

        return Result<IReadOnlyCollection<UserPermissionDto>>.Success(tenantPermissions);
    }

    /// <summary>
    /// A permission belongs to the current tenant if its resource is a tenant template,
    /// an application under a tenant template, the wildcard "Any" key, or a non-template
    /// resource type (User, Notifications) which are inherently tenant-scoped via membership.
    /// </summary>
    internal static bool BelongsToTenant(Permission permission, HashSet<TemplateId> tenantTemplateIds)
    {
        switch (permission.ResourceType)
        {
            case ResourceType.Template:
                if (string.Equals(permission.ResourceKey, Domain.Common.PermissionConstants.AnyResourceKey, StringComparison.OrdinalIgnoreCase))
                    return true;
                return Guid.TryParse(permission.ResourceKey, out var templateGuid)
                       && tenantTemplateIds.Contains(new TemplateId(templateGuid));

            case ResourceType.Application:
            case ResourceType.ApplicationFiles:
                if (string.Equals(permission.ResourceKey, Domain.Common.PermissionConstants.AnyResourceKey, StringComparison.OrdinalIgnoreCase))
                    return true;
                // Application permissions reference application GUIDs; filter via the
                // loaded Application→TemplateVersion→TemplateId if available, otherwise
                // keep (the resource validation on write already ensures tenant ownership).
                if (permission.Application?.TemplateVersion?.TemplateId is { } appTemplateId)
                    return tenantTemplateIds.Contains(appTemplateId);
                return true;

            case ResourceType.User:
            case ResourceType.Notifications:
            default:
                return true;
        }
    }
}
