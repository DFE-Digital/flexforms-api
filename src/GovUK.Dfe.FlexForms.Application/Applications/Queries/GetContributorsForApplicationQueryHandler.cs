using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Applications.Commands;
using GovUK.Dfe.FlexForms.Application.Applications.QueryObjects;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Applications.Queries;
public sealed record GetContributorsForApplicationQuery(
    Guid ApplicationId,
    bool IncludePermissionDetails = false) : IRequest<Result<IReadOnlyCollection<UserDto>>>;

public sealed class GetContributorsForApplicationQueryHandler(
    IEaRepository<Domain.Entities.Application> applicationRepo,
    IEaRepository<User> userRepo,
    IHttpContextAccessor httpContextAccessor,
    IPermissionCheckerService permissionCheckerService,
    ITenantPermissionFilter tenantPermissionFilter) : IRequestHandler<GetContributorsForApplicationQuery, Result<IReadOnlyCollection<UserDto>>>
{
    public async Task<Result<IReadOnlyCollection<UserDto>>> Handle(
        GetContributorsForApplicationQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext?.User is not ClaimsPrincipal user || !user.Identity?.IsAuthenticated == true)
                return Result<IReadOnlyCollection<UserDto>>.Forbid("Not authenticated");

            var principalId = user.FindFirstValue("appid") ?? user.FindFirstValue("azp");

            if (string.IsNullOrEmpty(principalId))
                principalId = user.FindFirstValue(ClaimTypes.Email);

            if (string.IsNullOrEmpty(principalId))
                return Result<IReadOnlyCollection<UserDto>>.Forbid("No user identifier");

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
                return Result<IReadOnlyCollection<UserDto>>.NotFound("User not found");

            // Get the application to verify it exists
            var applicationId = new ApplicationId(request.ApplicationId);
            var application = await (new GetApplicationByIdQueryObject(applicationId))
                .Apply(applicationRepo.Query().AsNoTracking())
                .FirstOrDefaultAsync(cancellationToken);

            if (application is null)
                return Result<IReadOnlyCollection<UserDto>>.NotFound("Application not found");

            // Check if user is the application owner or admin
            var isOwner = permissionCheckerService.IsApplicationOwner(application, dbUser.Id!.Value.ToString());
            var isAdmin = permissionCheckerService.IsAdmin();

            if (!isOwner && !isAdmin)
                return Result<IReadOnlyCollection<UserDto>>.Forbid("Only the application owner or admin can view contributors");

            if (!await tenantPermissionFilter.ApplicationBelongsToCurrentTenantAsync(request.ApplicationId, cancellationToken))
            {
                return Result<IReadOnlyCollection<UserDto>>.NotFound("Application not found");
            }

            // Get all contributors
            var contributors = await (new GetContributorsForApplicationQueryObject(applicationId))
                .Apply(userRepo.Query().AsNoTracking())
                .ToListAsync(cancellationToken);

            // Filter out the application creator
            var contributorsWithoutCreator = contributors
                .Where(c => c.Id != application.CreatedBy)
                .ToList();

            var contributorDtos = contributorsWithoutCreator.Select(c => new UserDto
            {
                UserId = c.Id!.Value,
                Name = c.Name,
                Email = c.Email,
                RoleId = c.RoleId.Value,
                Authorization = request.IncludePermissionDetails ? new UserAuthorizationDto
                {
                    Permissions = c.Permissions
                        .Select(p => new UserPermissionDto
                        {
                            ApplicationId = p.ApplicationId?.Value,
                            ResourceType = p.ResourceType,
                            ResourceKey = p.ResourceKey,
                            AccessType = p.AccessType
                        })
                        .ToArray(),
                    Roles = new List<string> { c.Role?.Name! }
                } : null
            }).ToList().AsReadOnly();

            return Result<IReadOnlyCollection<UserDto>>.Success(contributorDtos);
        }
        catch (Exception e)
        {
            return Result<IReadOnlyCollection<UserDto>>.Failure(e.Message);
        }
    }
}
