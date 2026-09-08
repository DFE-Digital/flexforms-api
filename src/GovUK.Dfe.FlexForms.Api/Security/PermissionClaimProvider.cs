using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Security.Interfaces;
using GovUK.Dfe.FlexForms.Application.Users.Queries;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace GovUK.Dfe.FlexForms.Api.Security
{
    /// <summary>
    /// Enriches Entra app-only (client-credential) principals with the mapped FlexForms
    /// user's role and permission claims. Mapping is <c>azp</c>/<c>appid</c> →
    /// <see cref="User.ExternalProviderId"/>. Interactive Entra users are left for
    /// <see cref="UserPermissionClaimProvider"/> (email lookup).
    /// </summary>
    public class PermissionsClaimProvider(
        ISender sender,
        ILogger<PermissionsClaimProvider> logger,
        IEaRepository<User> userRepo,
        IHttpContextAccessor httpContextAccessor) : ICustomClaimProvider
    {
        public async Task<IEnumerable<Claim>> GetClaimsAsync(ClaimsPrincipal principal)
        {
            var issuer = principal.FindFirst(JwtRegisteredClaimNames.Iss)?.Value
                         ?? principal.FindFirst("iss")?.Value;
            if (!EntraClientIdentity.IsEntraIssuer(issuer))
            {
                return Array.Empty<Claim>();
            }

            if (!EntraClientIdentity.IsAppOnlyToken(principal))
            {
                return Array.Empty<Claim>();
            }

            var httpContext = httpContextAccessor.HttpContext;
            if (!RequestClaimEnrichmentGate.TryBegin(
                    httpContext,
                    RequestClaimEnrichmentGate.AzurePermissionsKey))
            {
                return Array.Empty<Claim>();
            }

            var clientId = EntraClientIdentity.PickClientId(principal);
            if (string.IsNullOrEmpty(clientId))
            {
                logger.LogWarning("PermissionsClaimProvider() > Azure token had no azp/appid");
                return Array.Empty<Claim>();
            }

            var dbUser = await (new GetUserByExternalProviderIdQueryObject(clientId))
                    .Apply(userRepo.Query().AsNoTracking())
                    .FirstOrDefaultAsync();

            if (dbUser is null)
            {
                logger.LogDebug(
                    "PermissionsClaimProvider() > No EA user mapped to Azure client id {ClientId}. " +
                    "Unmapped Entra service callers rely on TenantAuthProvider roles only.",
                    clientId);
                return Array.Empty<Claim>();
            }

            if (dbUser.Role is null)
            {
                logger.LogWarning($"PermissionsClaimProvider() > Service User {dbUser.Id} has no role assigned");
                return Array.Empty<Claim>();
            }

            var query = new GetAllUserPermissionsQuery(dbUser.Id!);
            var result = await sender.Send(query);

            if (result is { IsSuccess: false })
            {
                logger.LogWarning($"PermissionsClaimProvider() > Failed to return the user permissions for Azure AppId:{clientId}");
                return Array.Empty<Claim>();
            }

            var claims = new List<Claim>();
            AddIdentityClaims(principal, dbUser, claims);

            var roleName = result.Value?.Roles?.FirstOrDefault() ?? dbUser.Role.Name;
            if (!string.IsNullOrEmpty(roleName))
            {
                claims.Add(new Claim(ClaimTypes.Role, roleName));
            }

            if (result.Value is not null)
            {
                claims.AddRange(result.Value.Permissions.Select(p =>
                    new Claim(
                        "permission",
                        $"{p.ResourceType}:{p.ResourceKey}:{p.AccessType}"
                    )
                ));
            }

            return claims;
        }

        private static void AddIdentityClaims(ClaimsPrincipal principal, User dbUser, List<Claim> claims)
        {
            if (!string.IsNullOrWhiteSpace(dbUser.Email)
                && principal.FindFirst(ClaimTypes.Email) is null
                && principal.FindFirst(TenantAuthClaimTypes.Email) is null)
            {
                claims.Add(new Claim(ClaimTypes.Email, dbUser.Email));
            }

            if (!string.IsNullOrWhiteSpace(dbUser.Name)
                && principal.FindFirst(ClaimTypes.Name) is null)
            {
                claims.Add(new Claim(ClaimTypes.Name, dbUser.Name));
            }
        }
    }
}
