using FluentValidation;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Applications.QueryObjects;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Users.Queries;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Common;
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
using System.Security.Claims;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Users.Commands;

/// <summary>
/// Request body for replacing a user's direct permission grants.
/// Kept here until <c>SetUserPermissionsRequest</c> is published in CoreLibs.Contracts.
/// </summary>
public sealed class SetUserPermissionsRequest
{
    public IReadOnlyCollection<RolePermissionGrantDto> Permissions { get; set; }
        = Array.Empty<RolePermissionGrantDto>();
}

/// <summary>
/// Replaces all direct (user-owned) permission grants for a tenant member.
/// Role-inherited permissions are unaffected.
/// </summary>
public sealed record SetUserPermissionsCommand(
    Guid UserId,
    IReadOnlyCollection<RolePermissionGrantDto> Permissions)
    : IRequest<Result<IReadOnlyCollection<UserPermissionDto>>>;

public sealed class SetUserPermissionsCommandValidator : AbstractValidator<SetUserPermissionsCommand>
{
    public SetUserPermissionsCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleForEach(x => x.Permissions).ChildRules(p =>
        {
            p.RuleFor(g => g.ResourceKey).NotEmpty().MaximumLength(256);
            p.RuleFor(g => g.ResourceType).IsInEnum();
            p.RuleFor(g => g.AccessType).IsInEnum();
            p.RuleFor(g => g).Custom((grant, context) =>
            {
                try
                {
                    RolePermissionGrantRules.EnsureValidForUser(
                        grant.ResourceType,
                        grant.ResourceKey,
                        grant.AccessType);
                }
                catch (ArgumentException ex)
                {
                    context.AddFailure(ex.Message);
                }
            });
        });
    }
}

public sealed class SetUserPermissionsCommandHandler(
    IPermissionCheckerService permissionCheckerService,
    ITenantContextAccessor tenantContextAccessor,
    ITenantMembershipService tenantMembershipService,
    IEaRepository<User> userRepository,
    IApplicationRepository applicationRepository,
    ITenantTemplateCatalogue tenantTemplateCatalogue,
    ITenantPermissionFilter tenantPermissionFilter,
    IUserFactory userFactory,
    IUnitOfWork unitOfWork,
    IUserCacheInvalidator userCacheInvalidator,
    IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<SetUserPermissionsCommand, Result<IReadOnlyCollection<UserPermissionDto>>>
{
    public async Task<Result<IReadOnlyCollection<UserPermissionDto>>> Handle(
        SetUserPermissionsCommand command,
        CancellationToken cancellationToken)
    {
        if (!permissionCheckerService.CanManageUsers())
            return Result<IReadOnlyCollection<UserPermissionDto>>.Forbid("Only administrators can manage user permissions");

        var tenant = tenantContextAccessor.CurrentTenant;
        if (tenant is null)
            return Result<IReadOnlyCollection<UserPermissionDto>>.Forbid("Tenant context is required");

        var userId = new UserId(command.UserId);
        var user = await new GetUserWithAllPermissionsByUserIdQueryObject(userId)
            .Apply(userRepository.Query())
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
            return Result<IReadOnlyCollection<UserPermissionDto>>.NotFound("User not found");

        var membership = await tenantMembershipService.GetActiveMembershipAsync(
            tenant.Id,
            userId,
            cancellationToken);

        if (membership is null)
            return Result<IReadOnlyCollection<UserPermissionDto>>.NotFound("User is not an active member of this tenant");

        var grantedById = await ResolveGrantedByUserIdAsync(cancellationToken);
        if (grantedById is null)
            return Result<IReadOnlyCollection<UserPermissionDto>>.Failure("Could not resolve the acting administrator");

        try
        {
            var grants = (command.Permissions ?? Array.Empty<RolePermissionGrantDto>()).ToList();

            foreach (var grant in grants)
            {
                RolePermissionGrantRules.EnsureValidForUser(
                    grant.ResourceType,
                    grant.ResourceKey,
                    grant.AccessType);
                var existenceError = await EnsureResourceExistsAsync(
                    grant.ResourceType,
                    grant.ResourceKey,
                    cancellationToken);
                if (existenceError is not null)
                    return Result<IReadOnlyCollection<UserPermissionDto>>.Failure(existenceError);
            }

            // Only remove permissions that belong to the current tenant;
            // permissions for other tenants are left untouched.
            var tenantPermissions = await tenantPermissionFilter.FilterToCurrentTenantAsync(
                user.Permissions,
                cancellationToken);

            foreach (var existing in tenantPermissions)
                userFactory.RemovePermissionFromUser(user, existing);

            foreach (var grant in grants)
            {
                var resourceKey = grant.ResourceKey.Trim();
                ApplicationId? applicationId = null;
                if ((grant.ResourceType is ResourceType.Application or ResourceType.ApplicationFiles)
                    && Guid.TryParse(resourceKey, out var applicationGuid)
                    && applicationGuid != Guid.Empty)
                {
                    applicationId = new ApplicationId(applicationGuid);
                }

                userFactory.AddPermissionToUser(
                    user,
                    resourceKey,
                    grant.ResourceType,
                    [grant.AccessType],
                    grantedById,
                    applicationId);
            }

            await unitOfWork.CommitAsync(cancellationToken);

            await userCacheInvalidator.InvalidateForUserAsync(
                user.Email,
                user.ExternalProviderId,
                userId,
                cancellationToken);

            return Result<IReadOnlyCollection<UserPermissionDto>>.Success(
                user.Permissions.Select(Map).ToList());
        }
        catch (InvalidOperationException ex)
        {
            return Result<IReadOnlyCollection<UserPermissionDto>>.Failure(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Result<IReadOnlyCollection<UserPermissionDto>>.Failure(ex.Message);
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
                    return null;

                var user = await new GetUserByEmailQueryObject(key.ToLowerInvariant())
                    .Apply(userRepository.Query().AsNoTracking())
                    .FirstOrDefaultAsync(cancellationToken);

                return user is null
                    ? $"User '{key}' was not found."
                    : null;
            }

            default:
                return null;
        }
    }

    private async Task<UserId?> ResolveGrantedByUserIdAsync(CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var email = principal?.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var adminUser = await new GetUserByEmailQueryObject(email)
            .Apply(userRepository.Query().AsNoTracking())
            .FirstOrDefaultAsync(cancellationToken);

        return adminUser?.Id;
    }

    internal static UserPermissionDto Map(Permission permission) => new()
    {
        ApplicationId = permission.ApplicationId?.Value,
        ResourceKey = permission.ResourceKey,
        ResourceType = permission.ResourceType,
        AccessType = permission.AccessType
    };
}
