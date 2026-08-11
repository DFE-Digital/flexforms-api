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
    ITenantPermissionFilter tenantPermissionFilter,
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

        var tenantPermissions = await tenantPermissionFilter.FilterToCurrentTenantAsync(
            user.Permissions,
            cancellationToken);

        return Result<IReadOnlyCollection<UserPermissionDto>>.Success(
            tenantPermissions.Select(SetUserPermissionsCommandHandler.Map).ToList());
    }
}
