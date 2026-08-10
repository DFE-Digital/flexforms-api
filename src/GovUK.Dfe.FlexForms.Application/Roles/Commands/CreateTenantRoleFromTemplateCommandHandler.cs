using FluentValidation;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.Roles.Commands;

public sealed record CreateTenantRoleFromTemplateCommand(string TemplateKey)
    : IRequest<Result<TenantRoleDto>>;

public sealed class CreateTenantRoleFromTemplateCommandValidator
    : AbstractValidator<CreateTenantRoleFromTemplateCommand>
{
    public CreateTenantRoleFromTemplateCommandValidator()
    {
        RuleFor(x => x.TemplateKey).NotEmpty().MaximumLength(64);
    }
}

/// <summary>
/// Creates a custom role from a Caseworker/Reviewer preset and applies its permission grants.
/// </summary>
public sealed class CreateTenantRoleFromTemplateCommandHandler(
    IPermissionCheckerService permissionCheckerService,
    ITenantContextAccessor tenantContextAccessor,
    ITenantRoleService tenantRoleService,
    IRolePermissionService rolePermissionService,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateTenantRoleFromTemplateCommand, Result<TenantRoleDto>>
{
    public async Task<Result<TenantRoleDto>> Handle(
        CreateTenantRoleFromTemplateCommand command,
        CancellationToken cancellationToken)
    {
        if (!permissionCheckerService.IsAdmin())
            return Result<TenantRoleDto>.Forbid("Only administrators can manage roles");

        var tenant = tenantContextAccessor.CurrentTenant;
        if (tenant is null)
            return Result<TenantRoleDto>.Forbid("Tenant context is required");

        var template = RoleTemplates.Get(command.TemplateKey);
        if (template is null)
        {
            return Result<TenantRoleDto>.Failure(
                $"Unknown role template '{command.TemplateKey}'. " +
                $"Supported: {string.Join(", ", RoleTemplates.All.Select(t => t.Key))}.");
        }

        try
        {
            var role = await tenantRoleService.CreateCustomRoleAsync(
                tenant.Id,
                template.DefaultRoleName,
                cancellationToken);

            var grants = template.Grants
                .Select(g => (g.ResourceType, g.ResourceKey, g.AccessType))
                .ToList();

            foreach (var grant in grants)
                RolePermissionGrantRules.EnsureValid(grant.ResourceType, grant.ResourceKey, grant.AccessType);

            await rolePermissionService.ReplacePermissionsAsync(role, grants, cancellationToken);
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
