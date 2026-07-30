using GovUK.Dfe.CoreLibs.Caching.Helpers;
using GovUK.Dfe.CoreLibs.Caching.Interfaces;
using GovUK.Dfe.CoreLibs.Security.Interfaces;
using GovUK.Dfe.FlexForms.Application.Common;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Api.Security;

/// <summary>
/// Enriches the principal with <c>permission</c> claims on every request:
/// role defaults (<see cref="RolePermission"/>) plus user overrides
/// (<see cref="Permission"/>, including <c>ResourceType.Template</c> form access).
/// When a user has any grant for a resource type+key, role grants for that key are omitted.
/// </summary>
public class UserPermissionClaimProvider(
    ILogger<UserPermissionClaimProvider> logger,
    IEaRepository<User> userRepo,
    ICacheService<IRedisCacheType> cacheService,
    ITenantContextAccessor tenantContextAccessor,
    ITenantMembershipService tenantMembershipService,
    IRolePermissionService rolePermissionService) : ICustomClaimProvider
{
    public async Task<IEnumerable<Claim>> GetClaimsAsync(ClaimsPrincipal principal)
    {
        var issuer = principal.FindFirst(JwtRegisteredClaimNames.Iss)?.Value
                     ?? principal.FindFirst("iss")?.Value;
        if (string.IsNullOrEmpty(issuer) || issuer.Contains("windows.net", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<Claim>();
        }

        var userEmail = principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrEmpty(userEmail))
        {
            logger.LogWarning("UserPermissionClaimProvider > User email not found.");
            return Array.Empty<Claim>();
        }

        var currentTenant = tenantContextAccessor.CurrentTenant;
        if (currentTenant is null)
        {
            logger.LogWarning("UserPermissionClaimProvider > No tenant context.");
            return Array.Empty<Claim>();
        }

        var baseCacheKey = $"UserClaims_{CacheKeyHelper.GenerateHashedCacheKey(userEmail.ToLowerInvariant())}";
        var cacheKey = TenantCacheKeyHelper.CreateTenantScopedKey(tenantContextAccessor, baseCacheKey);
        var methodName = nameof(UserPermissionClaimProvider);

        var permissionValues = await cacheService.GetOrAddAsync<List<string>>(
            cacheKey,
            async () =>
            {
                var dbUser = await new GetUserByEmailQueryObject(userEmail)
                    .Apply(userRepo.Query().AsNoTracking())
                    .FirstOrDefaultAsync();

                if (dbUser?.Id is null)
                    return new List<string>();

                var roleGrants = new List<PermissionClaimMerger.Grant>();

                var membership = await tenantMembershipService.GetActiveMembershipAsync(
                    currentTenant.Id,
                    dbUser.Id,
                    CancellationToken.None);

                if (membership?.RoleId is not null)
                {
                    var rolePerms = await rolePermissionService.GetByRoleIdAsync(
                        membership.RoleId,
                        CancellationToken.None);

                    roleGrants.AddRange(rolePerms.Select(rp =>
                        new PermissionClaimMerger.Grant(rp.ResourceType, rp.ResourceKey, rp.AccessType)));
                }

                var userWithPerms = await new GetUserWithAllPermissionsByUserIdQueryObject(dbUser.Id)
                    .Apply(userRepo.Query().AsNoTracking())
                    .FirstOrDefaultAsync();

                var userGrants = (userWithPerms?.Permissions ?? [])
                    .Select(p => new PermissionClaimMerger.Grant(p.ResourceType, p.ResourceKey, p.AccessType))
                    .ToList();

                return PermissionClaimMerger.Merge(roleGrants, userGrants).ToList();
            },
            methodName);

        return permissionValues.Select(v => new Claim("permission", v));
    }
}
