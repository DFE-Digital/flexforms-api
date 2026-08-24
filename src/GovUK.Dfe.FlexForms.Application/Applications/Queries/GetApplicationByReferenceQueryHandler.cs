using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Applications.QueryObjects;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Applications.Queries;

public sealed record GetApplicationByReferenceQuery(string ApplicationReference, bool IncludeSchema = true)
    : IRequest<Result<ApplicationDto>>;

public sealed class GetApplicationByReferenceQueryHandler(
    IApplicationRepository applicationRepo,
    IHttpContextAccessor httpContextAccessor,
    IPermissionCheckerService permissionCheckerService,
    ITenantPermissionFilter tenantPermissionFilter)
    : IRequestHandler<GetApplicationByReferenceQuery, Result<ApplicationDto>>
{
    public async Task<Result<ApplicationDto>> Handle(
        GetApplicationByReferenceQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext?.User is not ClaimsPrincipal user || !user.Identity?.IsAuthenticated == true)
                return Result<ApplicationDto>.Forbid("Not authenticated");

            var dto = await new GetApplicationByReferenceDtoQueryObject(request.ApplicationReference, request.IncludeSchema)
                .Apply(applicationRepo.Query())
                .FirstOrDefaultAsync(cancellationToken);

            if (dto is null)
                return Result<ApplicationDto>.NotFound("Application not found");

            if (!await tenantPermissionFilter.ApplicationBelongsToCurrentTenantAsync(
                    dto.ApplicationId,
                    cancellationToken))
            {
                return Result<ApplicationDto>.Forbid("Application does not belong to the current tenant");
            }

            if (dto.Status == ApplicationStatus.Deleted && !permissionCheckerService.IsAdmin())
            {
                return Result<ApplicationDto>.NotFound("Application not found");
            }

            var canAccess = permissionCheckerService.HasPermission(
                ResourceType.Application,
                dto.ApplicationId.ToString(),
                AccessType.Read);

            if (!canAccess)
                return Result<ApplicationDto>.Forbid("User does not have permission to read this application");

            var latestResponseEntity = await applicationRepo.GetLatestResponseAsync(
                new ApplicationId(dto.ApplicationId),
                cancellationToken);

            var latestResponse = latestResponseEntity is null
                ? null
                : MapLatestResponse(latestResponseEntity);

            return Result<ApplicationDto>.Success(dto with { LatestResponse = latestResponse });
        }
        catch (Exception e)
        {
            return Result<ApplicationDto>.Failure(e.Message);
        }
    }

    private static ApplicationResponseDetailsDto MapLatestResponse(Domain.Entities.ApplicationResponse response) =>
        new()
        {
            ResponseId = response.Id!.Value,
            ResponseBody = response.ResponseBody,
            CreatedOn = response.CreatedOn,
            CreatedBy = response.CreatedBy.Value,
            LastModifiedOn = response.LastModifiedOn,
            LastModifiedBy = response.LastModifiedBy?.Value
        };
}
