using GovUK.Dfe.FlexForms.Application.Applications.Commands;
using GovUK.Dfe.FlexForms.Application.Applications.QueryObjects;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Events;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Factories;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;
using GovUK.Dfe.FlexForms.Application.Common.Attributes;
using GovUK.Dfe.FlexForms.Application.Common.Behaviours;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;

namespace GovUK.Dfe.FlexForms.Application.Applications.Commands;

[RateLimit(2, 30)]
public sealed record AddContributorCommand(
    Guid ApplicationId,
    string Name,
    string Email) : IRequest<Result<UserDto>>, IRateLimitedRequest;

public sealed class AddContributorCommandHandler(
    IEaRepository<Domain.Entities.Application> applicationRepo,
    IEaRepository<User> userRepo,
    IHttpContextAccessor httpContextAccessor,
    IPermissionCheckerService permissionCheckerService,
    IUserFactory userFactory,
    IUserCacheInvalidator userCacheInvalidator,
    ITenantContextAccessor tenantContextAccessor,
    ITenantMembershipService tenantMembershipService,
    IUnitOfWork unitOfWork) : IRequestHandler<AddContributorCommand, Result<UserDto>>
{
    public async Task<Result<UserDto>> Handle(
        AddContributorCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext?.User is not ClaimsPrincipal user || !user.Identity?.IsAuthenticated == true)
                return Result<UserDto>.Forbid("Not authenticated");

            var currentTenant = tenantContextAccessor.CurrentTenant;
            if (currentTenant is null)
                return Result<UserDto>.Failure("Tenant could not be resolved for the current request.");

            var principalId = user.FindFirstValue("appid") ?? user.FindFirstValue("azp");

            if (string.IsNullOrEmpty(principalId))
                principalId = user.FindFirstValue(ClaimTypes.Email);

            if (string.IsNullOrEmpty(principalId))
                return Result<UserDto>.Forbid("No user identifier");

            User? dbUser;
            if (principalId.Contains('@'))
            {
                dbUser = await (new GetUserByEmailQueryObject(principalId))
                    .Apply(userRepo.Query().AsNoTracking())
                    .FirstOrDefaultAsync(cancellationToken);
            }
            else
            {
                dbUser = await (new GetUserByExternalProviderIdQueryObject(principalId))
                    .Apply(userRepo.Query().AsNoTracking())
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (dbUser is null)
                return Result<UserDto>.NotFound("User not found");

            // Get the application to verify it exists
            var applicationId = new ApplicationId(request.ApplicationId);
            var application = await (new GetApplicationByIdQueryObject(applicationId))
                .Apply(applicationRepo.Query().AsNoTracking())
                .FirstOrDefaultAsync(cancellationToken);

            if (application is null)
                return Result<UserDto>.NotFound("Application not found");

            // Check if user is the application owner or admin
            var isOwner = permissionCheckerService.IsApplicationOwner(application, dbUser.Id!.Value.ToString());
            var isAdmin = permissionCheckerService.IsAdmin();

            if (!isOwner && !isAdmin)
                return Result<UserDto>.Forbid("Only the application owner or admin can add contributors");

            // Load permissions so idempotent grants work (including Template form access).
            var existingContributor = await (new GetUserWithAllPermissionsByEmailQueryObject(request.Email))
                .Apply(userRepo.Query())
                .FirstOrDefaultAsync(cancellationToken);

            if (existingContributor != null)
            {
                return await HandleExistingContributor(
                    existingContributor,
                    applicationId,
                    application,
                    dbUser,
                    currentTenant.Id,
                    cancellationToken);
            }

            // Create new contributor using factory with User role
            var contributorId = new UserId(Guid.NewGuid());
            var now = DateTime.UtcNow;

            var contributor = userFactory.CreateContributor(
                contributorId,
                new RoleId(RoleConstants.UserRoleId),
                request.Name,
                request.Email,
                dbUser.Id!,
                applicationId,
                application.ApplicationReference,
                application.TemplateVersion!.TemplateId,
                now,
                currentTenant.Id);

            await userRepo.AddAsync(contributor, cancellationToken);

            // Required for token exchange on this tenant (same gate as self-registration).
            await EnsureMembershipForCurrentTenantAsync(
                currentTenant.Id,
                contributor.Id!,
                cancellationToken);

            await unitOfWork.CommitAsync(cancellationToken);

            await userCacheInvalidator.InvalidateForUserAsync(
                contributor.Email,
                contributor.ExternalProviderId,
                contributor.Id!,
                cancellationToken);

            // Create authorization data directly from the contributor instead of querying
            var authorization = CreateAuthorizationFromUser(contributor);

            return Result<UserDto>.Success(new UserDto
            {
                UserId = contributor.Id!.Value,
                Name = contributor.Name,
                Email = contributor.Email,
                RoleId = contributor.RoleId.Value,
                Authorization = authorization
            });
        }
        catch (Exception e)
        {
            return Result<UserDto>.Failure(e.Message);
        }
    }

    private async Task<Result<UserDto>> HandleExistingContributor(
        User existingContributor,
        ApplicationId applicationId,
        Domain.Entities.Application application,
        User dbUser,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        // Ensure self-service endpoints (e.g. GetMyPermissions) work for invited contributors
        userFactory.AddPermissionToUser(
            existingContributor,
            existingContributor.Email,
            ResourceType.User,
            new[] { AccessType.Read },
            dbUser.Id!,
            null,
            DateTime.UtcNow);

        // Application permissions
        userFactory.AddPermissionToUser(
            existingContributor,
            applicationId.Value.ToString(),
            ResourceType.Application,
            new[] { AccessType.Read, AccessType.Write },
            dbUser.Id!,
            applicationId,
            DateTime.UtcNow);

        // Application files permissions
        userFactory.AddPermissionToUser(
            existingContributor,
            applicationId.Value.ToString(),
            ResourceType.ApplicationFiles,
            new[] { AccessType.Read, AccessType.Write, AccessType.Delete },
            dbUser.Id!,
            applicationId,
            DateTime.UtcNow);

        // Notifications permissions
        userFactory.AddPermissionToUser(
            existingContributor,
            TenantScopedIdentityKey.Combine(tenantId, existingContributor.Email),
            ResourceType.Notifications,
            new[] { AccessType.Read, AccessType.Write, AccessType.Delete },
            dbUser.Id!,
            applicationId,
            DateTime.UtcNow);

        // Schema read for this form only. Do not grant Template:Write — that would let a
        // new invitee create other applications. AddPermission is additive: existing
        // Template:Write on this form, other forms, or other tenants is left untouched.
        userFactory.AddTemplatePermissionToUser(
            existingContributor,
            application.TemplateVersion!.TemplateId.Value.ToString(),
            new[] { AccessType.Read },
            dbUser.Id!,
            DateTime.UtcNow);

        // Raise event for existing contributor (for email side effects)
        existingContributor.AddDomainEvent(new ContributorPermissionsGrantedEvent(
            applicationId,
            application.ApplicationReference,
            application.TemplateVersion!.TemplateId,
            existingContributor,
            new[] { AccessType.Read, AccessType.Write },
            dbUser.Id!,
            DateTime.UtcNow));

        // Existing invitees also need TenantMembership or exchange returns "not a member".
        await EnsureMembershipForCurrentTenantAsync(
            tenantId,
            existingContributor.Id!,
            cancellationToken);

        await unitOfWork.CommitAsync(cancellationToken);

        await userCacheInvalidator.InvalidateForUserAsync(
            existingContributor.Email,
            existingContributor.ExternalProviderId,
            existingContributor.Id!,
            cancellationToken);

        // Create authorization data directly from the updated contributor
        var updatedAuthorization = CreateAuthorizationFromUser(existingContributor);

        return Result<UserDto>.Success(new UserDto
        {
            UserId = existingContributor.Id!.Value,
            Name = existingContributor.Name,
            Email = existingContributor.Email,
            RoleId = existingContributor.RoleId.Value,
            Authorization = updatedAuthorization
        });
    }

    /// <summary>
    /// Ensures an active TenantMembership exists so the invitee can exchange tokens on this tenant.
    /// Does not demote an existing higher role — only creates membership when none is active.
    /// </summary>
    private async Task EnsureMembershipForCurrentTenantAsync(
        Guid tenantId,
        UserId userId,
        CancellationToken cancellationToken)
    {
        var existing = await tenantMembershipService.GetActiveMembershipAsync(
            tenantId,
            userId,
            cancellationToken);
        if (existing is not null)
            return;

        await tenantMembershipService.UpsertMembershipAsync(
            tenantId,
            userId,
            RoleNames.User,
            cancellationToken);
    }

    private UserAuthorizationDto? CreateAuthorizationFromUser(User user)
    {
        if (user.Permissions == null || !user.Permissions.Any())
            return null;

        return new UserAuthorizationDto
        {
            Permissions = user.Permissions
                .Select(p => new UserPermissionDto
                {
                    ApplicationId = p.ApplicationId?.Value,
                    ResourceType = p.ResourceType,
                    ResourceKey = TenantScopedIdentityKey.ToClaimResourceKey(p.ResourceType, p.ResourceKey),
                    AccessType = p.AccessType
                })
                .ToArray(),
            Roles = new List<string> { user.Role?.Name ?? "User" }
        };
    }
}
