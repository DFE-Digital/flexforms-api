using GovUK.Dfe.CoreLibs.Caching.Helpers;
using GovUK.Dfe.CoreLibs.Caching.Interfaces;
using GovUK.Dfe.CoreLibs.Security.Interfaces;
using GovUK.Dfe.FlexForms.Application.Common;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Api.Security;

/// <summary>
/// Enriches the principal with <c>permission</c> claims on every request:
/// role defaults (<see cref="RolePermission"/>) plus user overrides
/// (<see cref="Permission"/>, including <c>ResourceType.Template</c> form access).
/// When a user has any grant for a resource type+key, role grants for that key are omitted.
/// User-owned grants are filtered to the current tenant before merge.
/// </summary>
public class UserPermissionClaimProvider(
    ILogger<UserPermissionClaimProvider> logger,
    IEaRepository<User> userRepo,
    ICacheService<IRedisCacheType> cacheService,
    ITenantContextAccessor tenantContextAccessor,
    ITenantMembershipService tenantMembershipService,
    IRolePermissionService rolePermissionService,
    ITenantPermissionFilter tenantPermissionFilter,
    IHttpContextAccessor httpContextAccessor) : ICustomClaimProvider
{
    public async Task<IEnumerable<Claim>> GetClaimsAsync(ClaimsPrincipal principal)
    {
        if (httpContextAccessor.HttpContext?.Items.ContainsKey(
                RequestClaimEnrichmentGate.AzurePermissionsKey) == true)
        {
            return Array.Empty<Claim>();
        }

        var userEmail = EntraClientIdentity.PickEmail(principal);
        var clientId = EntraClientIdentity.PickClientId(principal);
        if (string.IsNullOrEmpty(userEmail) && string.IsNullOrEmpty(clientId))
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

        var lookupKey = !string.IsNullOrEmpty(userEmail) ? userEmail : clientId!;
        var baseCacheKey = $"UserClaims_{CacheKeyHelper.GenerateHashedCacheKey(lookupKey.ToLowerInvariant())}";
        var cacheKey = TenantCacheKeyHelper.CreateTenantScopedKey(tenantContextAccessor, baseCacheKey);
        var methodName = nameof(UserPermissionClaimProvider);

        var permissionValues = await cacheService.GetOrAddAsync<List<string>>(
            cacheKey,
            async () =>
            {
                User? dbUser;
                if (!string.IsNullOrEmpty(userEmail))
                {
                    dbUser = await new GetUserByEmailQueryObject(userEmail)
                        .Apply(userRepo.Query().AsNoTracking())
                        .FirstOrDefaultAsync();
                }
                else
                {
                    dbUser = await new GetUserByExternalProviderIdQueryObject(clientId!)
                        .Apply(userRepo.Query().AsNoTracking())
                        .FirstOrDefaultAsync();
                }

                if (dbUser?.Id is null)
                    return new List<string>();

                var membership = await tenantMembershipService.GetActiveMembershipAsync(
                    currentTenant.Id,
                    dbUser.Id,
                    CancellationToken.None);

                var isPlatformSuperAdmin = RoleNames.IsPlatformSuperAdminUser(
                    dbUser.Role?.Name ?? RoleNames.FromRoleId(dbUser.RoleId.Value),
                    dbUser.RoleId.Value);

                // Suspended / removed members must not receive permission claims from a
                // stale JWT. Platform SuperAdmin may operate without a membership row.
                if (membership is null && !isPlatformSuperAdmin)
                {
                    logger.LogInformation(
                        "UserPermissionClaimProvider > No active membership for {LookupKey} on tenant {TenantName}; emitting no permission claims.",
                        lookupKey,
                        currentTenant.Name);
                    return new List<string>();
                }

                var roleGrants = new List<PermissionClaimMerger.Grant>();

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

                var tenantUserPermissions = await tenantPermissionFilter.FilterToCurrentTenantAsync(
                    userWithPerms?.Permissions ?? [],
                    CancellationToken.None);

                var userGrants = tenantUserPermissions
                    .Select(p => new PermissionClaimMerger.Grant(
                        p.ResourceType,
                        TenantScopedIdentityKey.ToClaimResourceKey(p.ResourceType, p.ResourceKey),
                        p.AccessType))
                    .ToList();

                return PermissionClaimMerger.Merge(roleGrants, userGrants).ToList();
            },
            methodName);

        return permissionValues.Select(v => new Claim("permission", v));
    }
}
