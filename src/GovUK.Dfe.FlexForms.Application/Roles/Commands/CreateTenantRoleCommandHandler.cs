using FluentValidation;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.Roles.Commands;

public sealed record CreateTenantRoleCommand(string Name) : IRequest<Result<TenantRoleDto>>;

public sealed class CreateTenantRoleCommandValidator : AbstractValidator<CreateTenantRoleCommand>
{
    public CreateTenantRoleCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(128);
    }
}

public sealed class CreateTenantRoleCommandHandler(
    IPermissionCheckerService permissionCheckerService,
    ITenantContextAccessor tenantContextAccessor,
    ITenantRoleService tenantRoleService,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateTenantRoleCommand, Result<TenantRoleDto>>
{
    public async Task<Result<TenantRoleDto>> Handle(
        CreateTenantRoleCommand command,
        CancellationToken cancellationToken)
    {
        if (!permissionCheckerService.IsAdmin())
            return Result<TenantRoleDto>.Forbid("Only administrators can manage roles");

        var tenant = tenantContextAccessor.CurrentTenant;
        if (tenant is null)
            return Result<TenantRoleDto>.Forbid("Tenant context is required");

        try
        {
            var role = await tenantRoleService.CreateCustomRoleAsync(
                tenant.Id,
                command.Name,
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            return Result<TenantRoleDto>.Success(Map(role));
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

    internal static TenantRoleDto Map(Domain.Entities.Role role) => new()
    {
        RoleId = role.Id!.Value,
        Name = role.Name,
        IsSystem = role.IsSystem
    };
}
