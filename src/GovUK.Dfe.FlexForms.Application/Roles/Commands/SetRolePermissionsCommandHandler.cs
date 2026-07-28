using FluentValidation;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Applications.QueryObjects;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

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
            p.RuleFor(g => g).Custom((grant, context) =>
            {
                try
                {
                    RolePermissionGrantRules.EnsureValid(grant.ResourceType, grant.ResourceKey, grant.AccessType);
                }
                catch (ArgumentException ex)
                {
                    context.AddFailure(ex.Message);
                }
            });
        });
    }
}

public sealed class SetRolePermissionsCommandHandler(
    IPermissionCheckerService permissionCheckerService,
    ITenantContextAccessor tenantContextAccessor,
    ITenantRoleService tenantRoleService,
    IRolePermissionService rolePermissionService,
    IApplicationRepository applicationRepository,
    ITenantTemplateCatalogue tenantTemplateCatalogue,
    IEaRepository<User> userRepository,
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

            foreach (var grant in grants)
            {
                RolePermissionGrantRules.EnsureValid(grant.ResourceType, grant.ResourceKey, grant.AccessType);
                var existenceError = await EnsureResourceExistsAsync(
                    grant.ResourceType,
                    grant.ResourceKey,
                    cancellationToken);
                if (existenceError is not null)
                    return Result<IReadOnlyCollection<RolePermissionDto>>.Failure(existenceError);
            }

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

    private async Task<string?> EnsureResourceExistsAsync(
        ResourceType resourceType,
        string resourceKey,
        CancellationToken cancellationToken)
    {
        var key = resourceKey.Trim();
        if (string.Equals(key, PermissionConstants.AnyResourceKey, StringComparison.OrdinalIgnoreCase))
            return null;

        switch (resourceType)
        {
            case ResourceType.Application:
            case ResourceType.ApplicationFiles:
            {
                if (!Guid.TryParse(key, out var applicationGuid))
                    return $"{resourceType} resource key must be a valid application id.";

                var application = await new GetApplicationByIdQueryObject(new ApplicationId(applicationGuid))
                    .Apply(applicationRepository.Query().AsNoTracking())
                    .FirstOrDefaultAsync(cancellationToken);

                if (application is null)
                    return $"Application '{key}' was not found.";

                var templateId = application.TemplateVersion?.TemplateId;
                if (templateId is null
                    || !await tenantTemplateCatalogue.ContainsAsync(templateId, cancellationToken))
                {
                    return $"Application '{key}' does not belong to the current tenant.";
                }

                return null;
            }

            case ResourceType.Template:
            {
                if (!Guid.TryParse(key, out var templateGuid))
                    return "Template resource key must be a valid template id.";

                if (!await tenantTemplateCatalogue.ContainsAsync(new TemplateId(templateGuid), cancellationToken))
                    return $"Template '{key}' was not found in the current tenant.";

                return null;
            }

            case ResourceType.User:
            case ResourceType.Notifications:
            {
                if (!key.Contains('@', StringComparison.Ordinal))
                    return null; // service client ids are opaque; skip user lookup

                var user = await new GetUserByEmailQueryObject(key.ToLowerInvariant())
                    .Apply(userRepository.Query().AsNoTracking())
                    .FirstOrDefaultAsync(cancellationToken);

                return user is null
                    ? $"User '{key}' was not found."
                    : null;
            }

            default:
                // File/Task/Page/Field/TaskGroup: shape-checked in domain; existence wiring can follow later.
                return null;
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
