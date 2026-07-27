using FluentValidation;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.Roles.Commands;

public sealed record SetRolePermissionsCommand(
    Guid RoleId,
    IReadOnlyCollection<RolePermissionGrantDto> Permissions)
    : IRequest<Result<IReadOnlyCollection<RolePermissionDto>>>;

public sealed class SetRolePermissionsCommandValidator : AbstractValidator<SetRolePermissionsCommand>
{
    public SetRolePermissionsCommandValidator()
    {
        RuleFor(x => x.RoleId).NotEmpty();
        RuleForEach(x => x.Permissions).ChildRules(p =>
        {
            p.RuleFor(g => g.ResourceKey).NotEmpty().MaximumLength(256);
            p.RuleFor(g => g.ResourceType).IsInEnum();
            p.RuleFor(g => g.AccessType).IsInEnum();
        });
    }
}

public sealed class SetRolePermissionsCommandHandler(
    IPermissionCheckerService permissionCheckerService,
    ITenantContextAccessor tenantContextAccessor,
    ITenantRoleService tenantRoleService,
    IRolePermissionService rolePermissionService,
    IUnitOfWork unitOfWork)
    : IRequestHandler<SetRolePermissionsCommand, Result<IReadOnlyCollection<RolePermissionDto>>>
{
    public async Task<Result<IReadOnlyCollection<RolePermissionDto>>> Handle(
        SetRolePermissionsCommand command,
        CancellationToken cancellationToken)
    {
        if (!permissionCheckerService.IsAdmin())
            return Result<IReadOnlyCollection<RolePermissionDto>>.Forbid("Only administrators can manage role permissions");

        var tenant = tenantContextAccessor.CurrentTenant;
        if (tenant is null)
            return Result<IReadOnlyCollection<RolePermissionDto>>.Forbid("Tenant context is required");

        var role = await tenantRoleService.GetByIdAsync(
            tenant.Id,
            new RoleId(command.RoleId),
            cancellationToken);

        if (role is null)
            return Result<IReadOnlyCollection<RolePermissionDto>>.NotFound("Role not found");

        try
        {
            var grants = (command.Permissions ?? Array.Empty<RolePermissionGrantDto>())
                .Select(p => (p.ResourceType, p.ResourceKey, p.AccessType))
                .ToList();

            await rolePermissionService.ReplacePermissionsAsync(role, grants, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            var saved = await rolePermissionService.GetByRoleIdAsync(role.Id!, cancellationToken);
            return Result<IReadOnlyCollection<RolePermissionDto>>.Success(
                saved.Select(Map).ToList());
        }
        catch (InvalidOperationException ex)
        {
            return Result<IReadOnlyCollection<RolePermissionDto>>.Failure(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Result<IReadOnlyCollection<RolePermissionDto>>.Failure(ex.Message);
        }
    }

    internal static RolePermissionDto Map(Domain.Entities.RolePermission permission) => new()
    {
        RolePermissionId = permission.Id!.Value,
        ResourceType = permission.ResourceType,
        ResourceKey = permission.ResourceKey,
        AccessType = permission.AccessType
    };
}
