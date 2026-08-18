using GovUK.Dfe.FlexForms.Application.Common;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.CoreLibs.Caching.Helpers;
using GovUK.Dfe.CoreLibs.Caching.Interfaces;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Application.Applications.Queries;

/// <summary>
/// Returns a paged list of all applications for the specified template.
/// </summary>
public sealed record GetApplicationsByTemplateQuery(
    Guid TemplateId,
    bool IncludeSchema = false,
    int? PageNumber = null,
    int? PageSize = null,
    ApplicationListingSearchCriteria? Search = null)
    : IRequest<Result<PagedResult<ApplicationDto>>>;

/// <summary>
/// Handles listing all applications for a template when the caller has tenant-wide or template-scoped read access.
/// </summary>
public sealed class GetApplicationsByTemplateQueryHandler(
    IHttpContextAccessor httpContextAccessor,
    IEaRepository<User> userRepo,
    IEaRepository<Domain.Entities.Application> appRepo,
    IApplicationRepository applicationRepository,
    ICacheService<IRedisCacheType> cacheService,
    ITenantContextAccessor tenantContextAccessor,
    ITenantTemplateResolver tenantTemplateResolver,
    IPermissionCheckerService permissionCheckerService)
    : IRequestHandler<GetApplicationsByTemplateQuery, Result<PagedResult<ApplicationDto>>>
{
    public async Task<Result<PagedResult<ApplicationDto>>> Handle(
        GetApplicationsByTemplateQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            var principal = httpContextAccessor.HttpContext?.User;
            if (principal is null || principal.Identity?.IsAuthenticated != true)
                return Result<PagedResult<ApplicationDto>>.Forbid("Not authenticated");

            var principalId = principal.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(principalId))
                principalId = principal.FindFirstValue("appid") ?? principal.FindFirstValue("azp");

            if (string.IsNullOrEmpty(principalId))
                return Result<PagedResult<ApplicationDto>>.Forbid("No user identifier");

            var templateId = new TemplateId(request.TemplateId);
            var searchKey = request.Search?.ToCacheKeySuffix() ?? "";
            var baseCacheKey =
                $"Applications_ByTemplate_{request.TemplateId}_{searchKey}_p{request.PageNumber}_ps{request.PageSize}_{CacheKeyHelper.GenerateHashedCacheKey(principalId)}_claimList";
            var cacheKey = TenantCacheKeyHelper.CreateTenantScopedKey(tenantContextAccessor, baseCacheKey);
            var methodName = nameof(GetApplicationsByTemplateQueryHandler);

            return await cacheService.GetOrAddAsync(
                cacheKey,
                async () =>
                {
                    var userWithAuthorization = await ResolveUserWithAuthorizationAsync(
                        principalId,
                        cancellationToken);

                    if (userWithAuthorization is null)
                        return Result<PagedResult<ApplicationDto>>.Forbid("User not found");

                    if (!await tenantTemplateResolver.IsTemplateInCurrentTenantAsync(templateId, cancellationToken))
                        return Result<PagedResult<ApplicationDto>>.Forbid(
                            "Template does not belong to the current tenant");

                    // Custom-role grants live on RolePermissions and are issued as JWT claims
                    // (Application:Any:Read). User.Permissions only has per-user overrides, so
                    // honour the claim as well as the DB resolver used for Admin / user-level Any.
                    var canListAll = permissionCheckerService.CanReadAllApplications()
                        || ApplicationAccessResolver.CanListAllApplicationsForTemplate(
                            userWithAuthorization,
                            templateId);

                    if (!canListAll)
                        return Result<PagedResult<ApplicationDto>>.Forbid(
                            "User does not have permission to list all applications for this template");

                    var query = ApplicationListingQueryBuilder.BuildTemplateQuery(
                        appRepo,
                        templateId,
                        request.Search?.Status);
                    query = ApplicationListingQueryBuilder.ApplySearchFilters(
                        query,
                        request.Search,
                        excludeStatus: request.Search?.Status is not null);

                    var pagedResult = await ApplicationListingQueryBuilder.MapPagedResultAsync(
                        query,
                        request.IncludeSchema,
                        request.PageNumber,
                        request.PageSize,
                        applicationRepository,
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

    private async Task<User?> ResolveUserWithAuthorizationAsync(
        string principalId,
        CancellationToken cancellationToken)
    {
        if (principalId.Contains('@'))
        {
            return await new GetUserWithAllPermissionsByEmailQueryObject(principalId)
                .Apply(userRepo.Query().AsNoTracking())
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await new GetUserWithAllPermissionsByExternalIdQueryObject(principalId)
            .Apply(userRepo.Query().AsNoTracking())
            .FirstOrDefaultAsync(cancellationToken);
    }
}
