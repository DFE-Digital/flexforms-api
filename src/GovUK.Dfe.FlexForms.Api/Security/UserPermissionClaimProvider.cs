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
/// role defaults (<see cref="RolePermission"/>) plus optional user overrides
/// (<see cref="Permission"/> / <see cref="TemplatePermission"/>).
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

                var claimValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Role defaults from TenantMembership → RolePermissions
                var membership = await tenantMembershipService.GetActiveMembershipAsync(
                    currentTenant.Id,
                    dbUser.Id,
                    CancellationToken.None);

                if (membership?.RoleId is not null)
                {
                    var rolePerms = await rolePermissionService.GetByRoleIdAsync(
                        membership.RoleId,
                        CancellationToken.None);

                    foreach (var rp in rolePerms)
                    {
                        claimValues.Add($"{rp.ResourceType}:{rp.ResourceKey}:{rp.AccessType}");
                    }
                }

                // User overrides (and legacy user-scoped grants)
                var userWithPerms = await new GetUserWithAllPermissionsByUserIdQueryObject(dbUser.Id)
                    .Apply(userRepo.Query().AsNoTracking())
                    .FirstOrDefaultAsync();

                foreach (var p in userWithPerms?.Permissions ?? [])
                {
                    claimValues.Add($"{p.ResourceType}:{p.ResourceKey}:{p.AccessType}");
                }

                foreach (var tp in userWithPerms?.TemplatePermissions ?? [])
                {
                    claimValues.Add($"Template:{tp.TemplateId.Value}:{tp.AccessType}");
                }

                return claimValues.ToList();
            },
            methodName);

        return permissionValues.Select(v => new Claim("permission", v));
    }
}
