using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Application.Users.Commands;

/// <summary>
/// Assigns a tenant role (system User or custom) to a user, creating the user when needed.
/// </summary>
public sealed record AssignUserRoleCommand(
    string Email,
    string Name,
    string Role,
    IReadOnlyCollection<Guid>? TemplateIds,
    bool CreateOnly = false)
    : IRequest<Result<UserDto>>;

/// <summary>
/// Handles administrative assignment of roles to users.
/// </summary>
public sealed class AssignUserRoleCommandHandler(
    IEaRepository<User> userRepo,
    IUnitOfWork unitOfWork,
    IPermissionCheckerService permissionCheckerService,
    IUserRoleProvisionerRegistry roleProvisionerRegistry,
    ITenantContextAccessor tenantContextAccessor,
    ITenantMembershipService tenantMembershipService,
    ITenantRoleService tenantRoleService,
    IUserFactory userFactory,
    IHttpContextAccessor httpContextAccessor,
    IUserCacheInvalidator userCacheInvalidator,
    ITenantAccessAuditWriter accessAuditWriter)
    : IRequestHandler<AssignUserRoleCommand, Result<UserDto>>
{
    public async Task<Result<UserDto>> Handle(
        AssignUserRoleCommand command,
        CancellationToken cancellationToken)
    {
        if (!permissionCheckerService.CanManageUsers())
            return Result<UserDto>.Forbid("Only administrators can assign roles");

        var currentTenant = tenantContextAccessor.CurrentTenant;
        if (currentTenant is null)
            return Result<UserDto>.Forbid("Tenant context is required to assign roles");

        if (string.IsNullOrWhiteSpace(command.Email))
            return Result<UserDto>.Failure("Email is required");

        if (string.IsNullOrWhiteSpace(command.Name))
            return Result<UserDto>.Failure("Name is required");

        if (string.IsNullOrWhiteSpace(command.Role))
            return Result<UserDto>.Failure("Role is required");

        if (RoleNames.IsReservedRoleName(command.Role) && RoleNames.ResolveAssignable(command.Role) is null)
        {
            return Result<UserDto>.Failure(
                $"Role '{command.Role}' is reserved for platform administrators and cannot be assigned to tenant users.");
        }

        var roleName = command.Role.Trim();
        var systemRole = RoleNames.ResolveAssignable(roleName);
        var isSystemAssignable = systemRole is not null;
        var membershipRoleName = systemRole ?? roleName;

        if (!isSystemAssignable)
        {
            var customRole = await tenantRoleService.GetByNameAsync(
                currentTenant.Id,
                membershipRoleName,
                cancellationToken);

            if (customRole is null)
            {
                return Result<UserDto>.Failure(
                    $"Role '{membershipRoleName}' was not found for this tenant. Create the custom role first.");
            }

            try
            {
                customRole.EnsureAssignableAsCustomRole();
            }
            catch (InvalidOperationException ex)
            {
                return Result<UserDto>.Failure(ex.Message);
            }
        }

        var templateIds = (command.TemplateIds ?? Array.Empty<Guid>())
            .Select(id => new TemplateId(id))
            .ToList();

        var grantedById = await ResolveGrantedByUserIdAsync(cancellationToken);
        if (grantedById is null)
            return Result<UserDto>.Failure("Could not resolve the acting administrator");

        var email = command.Email.Trim();
        var now = DateTime.UtcNow;
        var name = command.Name.Trim();

        var existingUser = await new GetUserWithAllPermissionsByEmailQueryObject(email)
            .Apply(userRepo.Query())
            .FirstOrDefaultAsync(cancellationToken);

        if (existingUser is not null)
        {
            if (command.CreateOnly && existingUser.Id is not null)
            {
                var activeMembership = await tenantMembershipService.GetActiveMembershipAsync(
                    currentTenant.Id,
                    existingUser.Id,
                    cancellationToken);

                if (activeMembership is not null)
                {
                    return Result<UserDto>.Failure(
                        "A user with this email address already exists in this tenant.");
                }
            }

            string? currentRoleName = null;
            if (existingUser.Id is not null)
            {
                var membership = await tenantMembershipService.GetActiveMembershipAsync(
                    currentTenant.Id,
                    existingUser.Id,
                    cancellationToken);
                currentRoleName = membership?.Role?.Name;
            }

            currentRoleName ??= existingUser.Role?.Name
                ?? RoleNames.FromRoleId(existingUser.RoleId.Value);

            if (RoleNames.IsPlatformSuperAdminUser(currentRoleName, existingUser.RoleId.Value))
            {
                return Result<UserDto>.Forbid(
                    "Cannot change a platform SuperAdmin membership through tenant role assignment");
            }

            if (RoleNames.IsDowngradeToUser(currentRoleName, membershipRoleName))
            {
                return Result<UserDto>.Forbid($"Cannot downgrade a {currentRoleName} to the User role");
            }
        }

        User user;
        try
        {
            if (isSystemAssignable)
            {
                var provisioner = roleProvisionerRegistry.GetProvisioner(membershipRoleName);
                if (provisioner is null)
                    return Result<UserDto>.Failure($"No provisioner is registered for role '{membershipRoleName}'");

                if (provisioner.RequiresTemplateIds && templateIds.Count == 0)
                    return Result<UserDto>.Failure($"At least one template ID is required for the {membershipRoleName} role");

                // Tenant Admin must use the per-tenant Admin role row — never the global
                // SuperAdmin RoleConstants.AdminRoleId (NULL TenantId). Seed system roles first
                // so brand-new tenants always have Admin/User before assignment.
                RoleId? tenantRoleId = null;
                if (string.Equals(membershipRoleName, RoleNames.Admin, StringComparison.OrdinalIgnoreCase))
                {
                    await tenantRoleService.EnsureSystemRolesAsync(currentTenant.Id, cancellationToken);

                    var tenantAdminRole = await tenantRoleService.GetOrCreateTenantRoleAsync(
                        currentTenant.Id,
                        RoleNames.Admin,
                        cancellationToken);

                    if (tenantAdminRole.Id is null)
                        return Result<UserDto>.Failure("Tenant Admin role was created without an identifier");

                    if (RoleNames.IsPlatformSuperAdminRoleId(tenantAdminRole.Id.Value))
                    {
                        return Result<UserDto>.Failure(
                            "Resolved Admin role is the platform SuperAdmin role; expected a tenant-scoped Admin role.");
                    }

                    tenantRoleId = tenantAdminRole.Id;
                }

                var assignmentRequest = new RoleAssignmentRequest(
                    name,
                    email,
                    templateIds,
                    grantedById,
                    now,
                    tenantRoleId,
                    currentTenant.Id);

                if (existingUser is null)
                {
                    user = provisioner.CreateUser(assignmentRequest);
                    await userRepo.AddAsync(user, cancellationToken);
                }
                else
                {
                    provisioner.AssignToExistingUser(existingUser, assignmentRequest);
                    user = existingUser;
                }
            }
            else
            {
                // Custom role: keep global User shell; capabilities come from RolePermissions + overrides.
                if (existingUser is null)
                {
                    if (templateIds.Count > 0)
                    {
                        user = userFactory.CreateStandardUser(
                            new UserId(Guid.NewGuid()),
                            name,
                            email,
                            templateIds,
                            grantedById,
                            now,
                            currentTenant.Id);
                    }
                    else
                    {
                        user = userFactory.CreateUser(
                            new UserId(Guid.NewGuid()),
                            new RoleId(RoleConstants.UserRoleId),
                            name,
                            email,
                            null,
                            now,
                            currentTenant.Id);
                        userFactory.AddPermissionToUser(
                            user,
                            email,
                            ResourceType.User,
                            [AccessType.Read],
                            grantedById,
                            null,
                            now);
                    }

                    await userRepo.AddAsync(user, cancellationToken);
                }
                else
                {
                    existingUser.AssignRole(new RoleId(RoleConstants.UserRoleId));
                    if (templateIds.Count > 0)
                    {
                        userFactory.GrantStandardUserAccess(existingUser, templateIds, grantedById, now, currentTenant.Id);
                    }

                    user = existingUser;
                }
            }
        }
        catch (ArgumentException ex)
        {
            return Result<UserDto>.Failure(ex.Message);
        }

        if (user.Id is null)
            return Result<UserDto>.Failure("User was created without an identifier");

        var upsertedMembership = await tenantMembershipService.UpsertMembershipAsync(
            currentTenant.Id,
            user.Id,
            membershipRoleName,
            cancellationToken);

        // Keep Users.RoleId aligned with the tenant membership role for Admin assignments.
        if (string.Equals(membershipRoleName, RoleNames.Admin, StringComparison.OrdinalIgnoreCase)
            && !RoleNames.IsPlatformSuperAdminRoleId(upsertedMembership.RoleId.Value))
        {
            user.AssignRole(upsertedMembership.RoleId);
        }

        await unitOfWork.CommitAsync(cancellationToken);

        var actorEmail = httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Email)
            ?? httpContextAccessor.HttpContext?.User?.Identity?.Name
            ?? "unknown";

        await accessAuditWriter.AppendAsync(
            currentTenant.Id,
            user.Id,
            user.Email,
            "RoleAssigned",
            membershipRoleName,
            grantedById,
            actorEmail,
            existingUser is null ? "User created and role assigned" : "Role assigned to existing user",
            cancellationToken);

        // Drop permission / OBO caches so the next request re-exchanges with the new role.
        await userCacheInvalidator.InvalidateForUserAsync(
            user.Email,
            user.ExternalProviderId,
            user.Id,
            cancellationToken);

        return Result<UserDto>.Success(new UserDto
        {
            UserId = user.Id!.Value,
            Name = user.Name,
            Email = user.Email,
            RoleId = user.RoleId.Value,
            Authorization = new UserAuthorizationDto
            {
                Permissions = user.Permissions.Select(p => new UserPermissionDto
                {
                    ApplicationId = p.ApplicationId?.Value,
                    ResourceType = p.ResourceType,
                    ResourceKey = p.ResourceKey,
                    AccessType = p.AccessType
                }).ToArray(),
                Roles = new[] { membershipRoleName }
            }
        });
    }

    private async Task<UserId?> ResolveGrantedByUserIdAsync(CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var email = principal?.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var adminUser = await (new GetUserByEmailQueryObject(email))
            .Apply(userRepo.Query().AsNoTracking())
            .FirstOrDefaultAsync(cancellationToken);

        return adminUser?.Id;
    }
}
