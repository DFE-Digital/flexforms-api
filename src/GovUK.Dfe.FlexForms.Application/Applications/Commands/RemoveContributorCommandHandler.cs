using GovUK.Dfe.FlexForms.Application.Applications.QueryObjects;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;

namespace GovUK.Dfe.FlexForms.Application.Applications.Commands;

public sealed record RemoveContributorCommand(
    Guid ApplicationId,
    Guid UserId) : IRequest<Result<bool>>;

public sealed class RemoveContributorCommandHandler(
    IEaRepository<Domain.Entities.Application> applicationRepo,
    IEaRepository<User> userRepo,
    IHttpContextAccessor httpContextAccessor,
    IPermissionCheckerService permissionCheckerService,
    IUserFactory userFactory,
    IUserCacheInvalidator userCacheInvalidator,
    ITenantPermissionFilter tenantPermissionFilter,
    IUnitOfWork unitOfWork) : IRequestHandler<RemoveContributorCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        RemoveContributorCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext?.User is not ClaimsPrincipal user || !user.Identity?.IsAuthenticated == true)
                return Result<bool>.Forbid("Not authenticated");

            var principalId = user.FindFirstValue("appid") ?? user.FindFirstValue("azp");

            if (string.IsNullOrEmpty(principalId))
                principalId = user.FindFirstValue(ClaimTypes.Email);

            if (string.IsNullOrEmpty(principalId))
                return Result<bool>.Forbid("No user identifier");

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
                return Result<bool>.NotFound("User not found");

            // Get the application to verify it exists
            var applicationId = new ApplicationId(request.ApplicationId);
            var application = await (new GetApplicationByIdQueryObject(applicationId))
                .Apply(applicationRepo.Query().AsNoTracking())
                .FirstOrDefaultAsync(cancellationToken);

            if (application is null)
                return Result<bool>.NotFound("Application not found");

            // Check if user is the application owner or admin
            var isOwner = permissionCheckerService.IsApplicationOwner(application, dbUser.Id!.Value.ToString());
            var isAdmin = permissionCheckerService.IsAdmin();

            if (!isOwner && !isAdmin)
                return Result<bool>.Forbid("Only the application owner or admin can remove contributors");

            if (!await tenantPermissionFilter.ApplicationBelongsToCurrentTenantAsync(request.ApplicationId, cancellationToken))
            {
                return Result<bool>.NotFound("Application not found");
            }

            // Get the contributor to remove
            var contributorId = new UserId(request.UserId);
            var contributor = await (new GetUserWithAllPermissionsByUserIdQueryObject(contributorId))
                .Apply(userRepo.Query())
                .FirstOrDefaultAsync(cancellationToken);

            if (contributor is null)
                return Result<bool>.NotFound("Contributor not found");

            // Check if contributor has permission for this application
            var permissions = contributor.Permissions
                .Where(p => p.ApplicationId == applicationId && p.ResourceType == ResourceType.Application)
                .ToList();

            if (!permissions.Any())
                return Result<bool>.Forbid("Contributor does not have permission for this application");

            // Remove all permissions for this application
            var allRemoved = true;
            foreach (var permission in permissions)
            {
                var removed = userFactory.RemovePermissionFromUser(contributor, permission);
                if (!removed)
                {
                    allRemoved = false;
                    break;
                }
            }

            if (!allRemoved)
                return Result<bool>.Failure("Failed to remove contributor permissions");

            await unitOfWork.CommitAsync(cancellationToken);

            await userCacheInvalidator.InvalidateForUserAsync(
                contributor.Email,
                contributor.ExternalProviderId,
                contributor.Id!,
                cancellationToken);

            return Result<bool>.Success(true);
        }
        catch (Exception e)
        {
            return Result<bool>.Failure(e.Message);
        }
    }
} 
