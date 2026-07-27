using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Application.Users.Commands;

/// <summary>
/// Assigns an assignable role to a user, creating the user when they do not already exist.
/// </summary>
public sealed record AssignUserRoleCommand(
    string Email,
    string Name,
    string Role,
    IReadOnlyCollection<Guid>? TemplateIds)
    : IRequest<Result<UserDto>>;

/// <summary>
/// Handles administrative assignment of predefined roles to users.
/// </summary>
public sealed class AssignUserRoleCommandHandler(
    IEaRepository<User> userRepo,
    IUnitOfWork unitOfWork,
    IPermissionCheckerService permissionCheckerService,
    IUserRoleProvisionerRegistry roleProvisionerRegistry,
    ITenantContextAccessor tenantContextAccessor,
    ITenantMembershipService tenantMembershipService,
    IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<AssignUserRoleCommand, Result<UserDto>>
{
    public async Task<Result<UserDto>> Handle(
        AssignUserRoleCommand command,
        CancellationToken cancellationToken)
    {
        if (!permissionCheckerService.IsAdmin())
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

        if (RoleNames.IsReservedRoleName(command.Role))
        {
            return Result<UserDto>.Failure(
                $"Role '{command.Role}' is reserved for platform administrators and cannot be assigned to tenant users.");
        }

        var canonicalRole = RoleNames.ResolveAssignable(command.Role);
        if (canonicalRole is null)
        {
            var allowed = string.Join(", ", RoleNames.Assignable);
            return Result<UserDto>.Failure($"Role '{command.Role}' is not assignable. Allowed roles: {allowed}");
        }

        var provisioner = roleProvisionerRegistry.GetProvisioner(canonicalRole);
        if (provisioner is null)
            return Result<UserDto>.Failure($"No provisioner is registered for role '{canonicalRole}'");

        var templateIds = (command.TemplateIds ?? Array.Empty<Guid>())
            .Select(id => new TemplateId(id))
            .ToList();

        if (provisioner.RequiresTemplateIds && templateIds.Count == 0)
            return Result<UserDto>.Failure($"At least one template ID is required for the {canonicalRole} role");

        var email = command.Email.Trim();
        var now = DateTime.UtcNow;

        var grantedById = await ResolveGrantedByUserIdAsync(cancellationToken);
        if (grantedById is null)
            return Result<UserDto>.Failure("Could not resolve the acting administrator");

        var assignmentRequest = new RoleAssignmentRequest(
            command.Name.Trim(),
            email,
            templateIds,
            grantedById,
            now);

        var existingUser = await new GetUserWithAllPermissionsByEmailQueryObject(email)
            .Apply(userRepo.Query())
            .FirstOrDefaultAsync(cancellationToken);

        if (existingUser is not null)
        {
            // Downgrade guard uses THIS tenant's membership role when present.
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

            if (RoleNames.IsSuperAdmin(currentRoleName) || RoleNames.IsReservedRoleName(currentRoleName))
            {
                return Result<UserDto>.Forbid(
                    "Cannot change a platform SuperAdmin membership through tenant role assignment");
            }

            if (RoleNames.IsDowngradeToUser(currentRoleName, canonicalRole))
            {
                var currentRole = RoleNames.ResolveAssignable(currentRoleName ?? string.Empty)
                    ?? currentRoleName;
                return Result<UserDto>.Forbid($"Cannot downgrade a {currentRole} to the User role");
            }
        }

        User user;
        try
        {
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
        catch (ArgumentException ex)
        {
            return Result<UserDto>.Failure(ex.Message);
        }

        if (user.Id is null)
            return Result<UserDto>.Failure("User was created without an identifier");

        await tenantMembershipService.UpsertMembershipAsync(
            currentTenant.Id,
            user.Id,
            canonicalRole,
            cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);

        var assignedRoleName = canonicalRole;

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
                Roles = new[] { assignedRoleName }
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
