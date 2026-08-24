using GovUK.Dfe.CoreLibs.Caching.Helpers;
using GovUK.Dfe.CoreLibs.Caching.Interfaces;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Common;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.Applications.Queries;

public sealed record GetApplicationsForUserQuery(
    string Email,
    bool IncludeSchema = false,
    Guid? TemplateId = null,
    int? PageNumber = null,
    int? PageSize = null,
    ApplicationListingSearchCriteria? Search = null)
    : IRequest<Result<PagedResult<ApplicationDto>>>;

public sealed class GetApplicationsForUserQueryHandler(
    IEaRepository<User> userRepo,
    IEaRepository<Domain.Entities.Application> appRepo,
    IPermissionCheckerService permissionCheckerService,
    IApplicationRepository applicationRepository,
    ICacheService<IRedisCacheType> cacheService,
    ITenantContextAccessor tenantContextAccessor,
    IUserAccessibleTemplateService userAccessibleTemplateService,
    ILogger<GetApplicationsForUserQueryHandler> logger)
    : IRequestHandler<GetApplicationsForUserQuery, Result<PagedResult<ApplicationDto>>>
{
    public async Task<Result<PagedResult<ApplicationDto>>> Handle(
        GetApplicationsForUserQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            var tenantName = tenantContextAccessor.CurrentTenant?.Name ?? "(none)";
            var searchKey = request.Search?.ToCacheKeySuffix() ?? "";
            var emailCacheKey = request.Email.Trim().ToLowerInvariant();
            var baseCacheKey =
                $"Applications_ForUser_{CacheKeyHelper.GenerateHashedCacheKey(emailCacheKey)}_t{request.TemplateId}_{searchKey}_p{request.PageNumber}_ps{request.PageSize}";
            var cacheKey = TenantCacheKeyHelper.CreateTenantScopedKey(tenantContextAccessor, baseCacheKey);
            var methodName = nameof(GetApplicationsForUserQueryHandler);

            return await cacheService.GetOrAddAsync(
                cacheKey,
                async () =>
                {
                    var dbUser = await new GetUserByEmailQueryObject(request.Email)
                        .Apply(userRepo.Query().AsNoTracking())
                        .FirstOrDefaultAsync(cancellationToken);

                    if (dbUser is null)
                    {
                        logger.LogWarning(
                            "Application listing: user not found. Tenant={Tenant}, Email={Email}",
                            tenantName,
                            request.Email);
                        return Result<PagedResult<ApplicationDto>>.Failure("GetApplicationsForUserQueryHandler > User not found.");
                    }

                    // Slim path: distinct application IDs only — no full Permissions Include.
                    var applicationIds = await new GetApplicationIdsByUserIdQueryObject(dbUser.Id!)
                        .Apply(userRepo.Query().AsNoTracking())
                        .ToListAsync(cancellationToken);

                    // Template grants only (for listing filter). Admins may skip via CanManageTemplates.
                    var templatePermissions = await new GetTemplateResourcePermissionsByUserIdQueryObject(dbUser.Id!)
                        .Apply(userRepo.Query().AsNoTracking())
                        .ToListAsync(cancellationToken);

                    var templateIdsFilter = await userAccessibleTemplateService.ResolveAccessibleListingFilterAsync(
                        templatePermissions,
                        request.TemplateId,
                        cancellationToken);

                    logger.LogInformation(
                        "My applications listing (own applications only). Tenant={Tenant}, Email={Email}, Role={Role}, ExplicitApplicationCount={ApplicationCount}, RequestedTemplateId={RequestedTemplateId}, EffectiveTemplateCount={EffectiveTemplateCount}",
                        tenantName,
                        request.Email,
                        dbUser.Role?.Name ?? "(unknown)",
                        applicationIds.Count,
                        request.TemplateId,
                        templateIdsFilter.Count);

                    var query = ApplicationListingQueryBuilder.BuildMyApplicationsQuery(
                        appRepo,
                        applicationIds,
                        templateIdsFilter);

                    query = ApplicationListingQueryBuilder.ApplySearchFilters(query, request.Search, request.Search?.Status == ApplicationStatus.Deleted && !permissionCheckerService.IsAdmin());

                    var pagedResult = await ApplicationListingQueryBuilder.MapPagedResultAsync(
                        query,
                        request.IncludeSchema,
                        request.PageNumber,
                        request.PageSize,
                        applicationRepository,
                        cancellationToken);

                    logger.LogInformation(
                        "Application listing completed. Tenant={Tenant}, Email={Email}, ReturnedCount={ReturnedCount}, TotalCount={TotalCount}",
                        tenantName,
                        request.Email,
                        pagedResult.Items.Count,
                        pagedResult.TotalCount);

                    return Result<PagedResult<ApplicationDto>>.Success(pagedResult);
                },
                methodName);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Application listing failed for {Email}", request.Email);
            return Result<PagedResult<ApplicationDto>>.Failure(e.ToString());
        }
    }
}
