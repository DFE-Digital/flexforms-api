using GovUK.Dfe.CoreLibs.Caching.Helpers;
using GovUK.Dfe.CoreLibs.Caching.Interfaces;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Common;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.TemplatePermissions.Queries
{
    public sealed record GetTemplatePermissionsForUserByUserIdQuery(UserId UserId)
        : IRequest<Result<IReadOnlyCollection<TemplatePermissionDto>>>;

    public sealed class GetTemplatePermissionsForUserByUserIdQueryHandler(
        IEaRepository<User> userRepo,
        ICacheService<IRedisCacheType> cacheService,
        ITenantContextAccessor tenantContextAccessor)
        : IRequestHandler<GetTemplatePermissionsForUserByUserIdQuery, Result<IReadOnlyCollection<TemplatePermissionDto>>>
    {
        public async Task<Result<IReadOnlyCollection<TemplatePermissionDto>>> Handle(
            GetTemplatePermissionsForUserByUserIdQuery request,
            CancellationToken cancellationToken)
        {
            try
            {
                var baseCacheKey = $"Template_Permissions_ByUiD_{CacheKeyHelper.GenerateHashedCacheKey(request.UserId.Value.ToString())}";
                var cacheKey = TenantCacheKeyHelper.CreateTenantScopedKey(tenantContextAccessor, baseCacheKey);

                var methodName = nameof(GetTemplatePermissionsForUserByUserIdQueryHandler);

                return await cacheService.GetOrAddAsync(
                    cacheKey,
                    async () =>
                    {
                        var user = await new GetUserWithAllPermissionsByUserIdQueryObject(request.UserId)
                            .Apply(userRepo.Query().AsNoTracking())
                            .FirstOrDefaultAsync(cancellationToken);

                        if (user is null)
                        {
                            return Result<IReadOnlyCollection<TemplatePermissionDto>>.Success(
                                Array.Empty<TemplatePermissionDto>());
                        }

                        var dtoList = UserTemplateAccess.GetTemplateGrants(user)
                            .Select(p => new TemplatePermissionDto
                            {
                                TemplatePermissionId = p.Id?.Value,
                                UserId = user.Id!.Value,
                                TemplateId = Guid.TryParse(p.ResourceKey, out var templateId)
                                    ? templateId
                                    : Guid.Empty,
                                AccessType = p.AccessType
                            })
                            .Where(p => p.TemplateId != Guid.Empty)
                            .ToList()
                            .AsReadOnly();

                        return Result<IReadOnlyCollection<TemplatePermissionDto>>.Success(dtoList);
                    },
                    methodName);
            }
            catch (Exception e)
            {
                return Result<IReadOnlyCollection<TemplatePermissionDto>>.Failure(e.ToString());
            }
        }
    }
}
