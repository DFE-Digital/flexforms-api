using FluentValidation;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.Roles.Commands;

public sealed record RenameTenantRoleCommand(Guid RoleId, string Name)
    : IRequest<Result<TenantRoleDto>>;

public sealed class RenameTenantRoleCommandValidator : AbstractValidator<RenameTenantRoleCommand>
{
    public RenameTenantRoleCommandValidator()
    {
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(128);
    }
}

public sealed class RenameTenantRoleCommandHandler(
    IPermissionCheckerService permissionCheckerService,
    ITenantContextAccessor tenantContextAccessor,
    ITenantRoleService tenantRoleService,
    IUnitOfWork unitOfWork)
    : IRequestHandler<RenameTenantRoleCommand, Result<TenantRoleDto>>
{
    public async Task<Result<TenantRoleDto>> Handle(
        RenameTenantRoleCommand command,
        CancellationToken cancellationToken)
    {
        if (!permissionCheckerService.IsAdmin())
            return Result<TenantRoleDto>.Forbid("Only administrators can manage roles");

        var tenant = tenantContextAccessor.CurrentTenant;
        if (tenant is null)
            return Result<TenantRoleDto>.Forbid("Tenant context is required");

        var role = await tenantRoleService.GetByIdAsync(
            tenant.Id,
            new RoleId(command.RoleId),
            cancellationToken);

        if (role is null)
            return Result<TenantRoleDto>.NotFound("Role not found");

        try
        {
            await tenantRoleService.RenameAsync(role, command.Name, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<TenantRoleDto>.Success(CreateTenantRoleCommandHandler.Map(role));
        }
        catch (InvalidOperationException ex)
        {
            return Result<TenantRoleDto>.Failure(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Result<TenantRoleDto>.Failure(ex.Message);
        }
    }
}
