using FluentValidation;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.Roles.Commands;

public sealed record DeleteTenantRoleCommand(Guid RoleId) : IRequest<Result<bool>>;

public sealed class DeleteTenantRoleCommandValidator : AbstractValidator<DeleteTenantRoleCommand>
{
    public DeleteTenantRoleCommandValidator()
    {
        RuleFor(x => x.RoleId).NotEmpty();
    }
}

public sealed class DeleteTenantRoleCommandHandler(
    IPermissionCheckerService permissionCheckerService,
    ITenantContextAccessor tenantContextAccessor,
    ITenantRoleService tenantRoleService,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteTenantRoleCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        DeleteTenantRoleCommand command,
        CancellationToken cancellationToken)
    {
        if (!permissionCheckerService.IsAdmin())
            return Result<bool>.Forbid("Only administrators can manage roles");

        var tenant = tenantContextAccessor.CurrentTenant;
        if (tenant is null)
            return Result<bool>.Forbid("Tenant context is required");

        var role = await tenantRoleService.GetByIdAsync(
            tenant.Id,
            new RoleId(command.RoleId),
            cancellationToken);

        if (role is null)
            return Result<bool>.NotFound("Role not found");

        try
        {
            await tenantRoleService.DeleteAsync(role, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<bool>.Success(true);
        }
        catch (InvalidOperationException ex)
        {
            return Result<bool>.Failure(ex.Message);
        }
    }
}
