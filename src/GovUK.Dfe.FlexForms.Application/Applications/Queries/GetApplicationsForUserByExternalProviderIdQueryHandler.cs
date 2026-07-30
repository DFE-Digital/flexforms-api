using GovUK.Dfe.CoreLibs.Caching.Helpers;
using GovUK.Dfe.CoreLibs.Caching.Interfaces;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Common;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Applications.Queries;

public sealed record GetApplicationsForUserByExternalProviderIdQuery(
    string ExternalProviderId,
    bool IncludeSchema = false,
    Guid? TemplateId = null,
    int? PageNumber = null,
    int? PageSize = null,
    ApplicationListingSearchCriteria? Search = null)
    : IRequest<Result<PagedResult<ApplicationDto>>>;

public sealed class GetApplicationsForUserByExternalProviderIdQueryHandler(
    IEaRepository<User> userRepo,
    IEaRepository<Domain.Entities.Application> appRepo,
    ICacheService<IRedisCacheType> cacheService,
    ITenantContextAccessor tenantContextAccessor,
    IUserAccessibleTemplateService userAccessibleTemplateService)
    : IRequestHandler<GetApplicationsForUserByExternalProviderIdQuery, Result<PagedResult<ApplicationDto>>>
{
    public async Task<Result<PagedResult<ApplicationDto>>> Handle(
        GetApplicationsForUserByExternalProviderIdQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            var searchKey = request.Search?.ToCacheKeySuffix() ?? "";
            var baseCacheKey =
                $"Applications_ForUserExternal_{CacheKeyHelper.GenerateHashedCacheKey(request.ExternalProviderId)}_t{request.TemplateId}_{searchKey}_p{request.PageNumber}_ps{request.PageSize}";
            var cacheKey = TenantCacheKeyHelper.CreateTenantScopedKey(tenantContextAccessor, baseCacheKey);
            var methodName = nameof(GetApplicationsForUserByExternalProviderIdQueryHandler);

            return await cacheService.GetOrAddAsync(
                cacheKey,
                async () =>
                {
                    var userWithAuthorization = await new GetUserWithAllPermissionsByExternalIdQueryObject(request.ExternalProviderId)
                        .Apply(userRepo.Query().AsNoTracking())
                        .FirstOrDefaultAsync(cancellationToken);

                    if (userWithAuthorization is null)
                        return Result<PagedResult<ApplicationDto>>.Success(
                            ApplicationListingQueryBuilder.EmptyPagedResult(request.PageNumber, request.PageSize));

                    var templateIdsFilter = await userAccessibleTemplateService.ResolveAccessibleListingFilterAsync(
                        userWithAuthorization.Permissions,
                        request.TemplateId,
                        cancellationToken);

                    var query = ApplicationListingQueryBuilder.BuildMyApplicationsQuery(
                        appRepo,
                        userWithAuthorization,
                        templateIdsFilter);

                    query = ApplicationListingQueryBuilder.ApplySearchFilters(query, request.Search);

                    var pagedResult = await ApplicationListingQueryBuilder.MapPagedResultAsync(
                        query,
                        request.IncludeSchema,
                        request.PageNumber,
                        request.PageSize,
                        cancellationToken);

                    return Result<PagedResult<ApplicationDto>>.Success(pagedResult);
                },
                methodName);
        }
        catch (Exception e)
        {
            return Result<PagedResult<ApplicationDto>>.Failure(e.ToString());
        }
    }
}
