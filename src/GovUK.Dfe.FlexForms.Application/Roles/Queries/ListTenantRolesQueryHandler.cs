using GovUK.Dfe.FlexForms.Application.Roles.Commands;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.Roles.Queries;

public sealed record ListTenantRolesQuery : IRequest<Result<IReadOnlyCollection<TenantRoleDto>>>;

public sealed class ListTenantRolesQueryHandler(
    IPermissionCheckerService permissionCheckerService,
    ITenantContextAccessor tenantContextAccessor,
    ITenantRoleService tenantRoleService,
    IUnitOfWork unitOfWork)
    : IRequestHandler<ListTenantRolesQuery, Result<IReadOnlyCollection<TenantRoleDto>>>
{
    public async Task<Result<IReadOnlyCollection<TenantRoleDto>>> Handle(
        ListTenantRolesQuery request,
        CancellationToken cancellationToken)
    {
        if (!permissionCheckerService.IsAdmin())
            return Result<IReadOnlyCollection<TenantRoleDto>>.Forbid("Only administrators can list roles");

        var tenant = tenantContextAccessor.CurrentTenant;
        if (tenant is null)
            return Result<IReadOnlyCollection<TenantRoleDto>>.Forbid("Tenant context is required");

        // New tenants only have SQL config rows — seed Admin/User before listing.
        await tenantRoleService.EnsureSystemRolesAsync(tenant.Id, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        var roles = await tenantRoleService.ListAsync(tenant.Id, cancellationToken);
        return Result<IReadOnlyCollection<TenantRoleDto>>.Success(
            roles.Select(CreateTenantRoleCommandHandler.Map).ToList());
    }
}

public sealed record GetRolePermissionsQuery(Guid RoleId)
    : IRequest<Result<IReadOnlyCollection<RolePermissionDto>>>;

public sealed class GetRolePermissionsQueryHandler(
    IPermissionCheckerService permissionCheckerService,
    ITenantContextAccessor tenantContextAccessor,
    ITenantRoleService tenantRoleService,
    IRolePermissionService rolePermissionService)
    : IRequestHandler<GetRolePermissionsQuery, Result<IReadOnlyCollection<RolePermissionDto>>>
{
    public async Task<Result<IReadOnlyCollection<RolePermissionDto>>> Handle(
        GetRolePermissionsQuery request,
        CancellationToken cancellationToken)
    {
        if (!permissionCheckerService.IsAdmin())
            return Result<IReadOnlyCollection<RolePermissionDto>>.Forbid("Only administrators can view role permissions");

        var tenant = tenantContextAccessor.CurrentTenant;
        if (tenant is null)
            return Result<IReadOnlyCollection<RolePermissionDto>>.Forbid("Tenant context is required");

        var role = await tenantRoleService.GetByIdAsync(
            tenant.Id,
            new RoleId(request.RoleId),
            cancellationToken);

        if (role is null)
            return Result<IReadOnlyCollection<RolePermissionDto>>.NotFound("Role not found");

        var permissions = await rolePermissionService.GetByRoleIdAsync(role.Id!, cancellationToken);
        return Result<IReadOnlyCollection<RolePermissionDto>>.Success(
            permissions.Select(SetRolePermissionsCommandHandler.Map).ToList());
    }
}
