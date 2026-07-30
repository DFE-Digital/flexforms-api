using GovUK.Dfe.CoreLibs.Caching.Helpers;
using GovUK.Dfe.CoreLibs.Caching.Interfaces;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Common;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Users.Queries
{
    public sealed record GetAllUserPermissionsQuery(UserId UserId)
        : IRequest<Result<UserAuthorizationDto>>;

    public sealed class GetAllUserPermissionsQueryHandler(
        IEaRepository<User> userRepo,
        ICacheService<IRedisCacheType> cacheService,
        ITenantContextAccessor tenantContextAccessor,
        ITenantMembershipService tenantMembershipService,
        IRolePermissionService rolePermissionService)
        : IRequestHandler<GetAllUserPermissionsQuery, Result<UserAuthorizationDto>>
    {
        public async Task<Result<UserAuthorizationDto>> Handle(
            GetAllUserPermissionsQuery request,
            CancellationToken cancellationToken)
        {
            try
            {
                var baseCacheKey = $"Permissions_All_UserId_{CacheKeyHelper.GenerateHashedCacheKey(request.UserId.Value.ToString())}";
                var cacheKey = TenantCacheKeyHelper.CreateTenantScopedKey(tenantContextAccessor, baseCacheKey);

                var methodName = nameof(GetAllUserPermissionsQueryHandler);

                return await cacheService.GetOrAddAsync(
                    cacheKey,
                    async () =>
                    {
                        var userWithPermissions = await new GetUserWithAllPermissionsByUserIdQueryObject(request.UserId)
                            .Apply(userRepo.Query().AsNoTracking())
                            .FirstOrDefaultAsync(cancellationToken);

                        if (userWithPermissions is null)
                        {
                            return Result<UserAuthorizationDto>.Success(new UserAuthorizationDto()
                            {
                                Permissions = Array.Empty<UserPermissionDto>(),
                                Roles = Array.Empty<string>(),
                            });
                        }

                        var roleGrants = new List<PermissionClaimMerger.Grant>();
                        string? membershipRoleName = null;
                        var currentTenant = tenantContextAccessor.CurrentTenant;

                        if (currentTenant is not null && userWithPermissions.Id is not null)
                        {
                            var membership = await tenantMembershipService.GetActiveMembershipAsync(
                                currentTenant.Id,
                                userWithPermissions.Id,
                                cancellationToken);

                            membershipRoleName = membership?.Role?.Name;

                            if (membership?.RoleId is not null)
                            {
                                var rolePerms = await rolePermissionService.GetByRoleIdAsync(
                                    membership.RoleId,
                                    cancellationToken);

                                roleGrants.AddRange(rolePerms.Select(rp =>
                                    new PermissionClaimMerger.Grant(rp.ResourceType, rp.ResourceKey, rp.AccessType)));
                            }
                        }

                        var userGrants = userWithPermissions.Permissions
                            .Select(p => new PermissionClaimMerger.Grant(p.ResourceType, p.ResourceKey, p.AccessType));

                        var mergedClaims = PermissionClaimMerger.Merge(roleGrants, userGrants);

                        // Preserve ApplicationId on user-owned application grants for web consumers.
                        var applicationIdsByKey = userWithPermissions.Permissions
                            .Where(p => p.ApplicationId is not null)
                            .GroupBy(p => $"{p.ResourceType}:{p.ResourceKey}:{p.AccessType}", StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(g => g.Key, g => g.First().ApplicationId!.Value, StringComparer.OrdinalIgnoreCase);

                        var permissions = mergedClaims
                            .Select(claim => ParsePermissionClaim(claim, applicationIdsByKey))
                            .Where(p => p is not null)
                            .Select(p => p!)
                            .ToArray();

                        var roleName = membershipRoleName
                            ?? userWithPermissions.Role?.Name
                            ?? RoleNames.FromRoleId(userWithPermissions.RoleId.Value)
                            ?? RoleNames.User;

                        var userAuthzDto = new UserAuthorizationDto
                        {
                            Permissions = permissions,
                            Roles = new List<string> { roleName }
                        };

                        return Result<UserAuthorizationDto>.Success(userAuthzDto);
                    },
                    methodName);
            }
            catch (Exception e)
            {
                return Result<UserAuthorizationDto>.Failure(e.ToString());
            }
        }

        private static UserPermissionDto? ParsePermissionClaim(
            string claim,
            IReadOnlyDictionary<string, Guid> applicationIdsByKey)
        {
            var parts = claim.Split(':', 3);
            if (parts.Length != 3)
                return null;

            if (!Enum.TryParse<ResourceType>(parts[0], ignoreCase: true, out var resourceType))
                return null;

            if (!Enum.TryParse<AccessType>(parts[2], ignoreCase: true, out var accessType))
                return null;

            applicationIdsByKey.TryGetValue(claim, out var applicationId);

            return new UserPermissionDto
            {
                ApplicationId = applicationIdsByKey.ContainsKey(claim) ? applicationId : null,
                ResourceType = resourceType,
                ResourceKey = parts[1],
                AccessType = accessType
            };
        }
    }
}
