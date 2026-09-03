using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Security.Interfaces;
using GovUK.Dfe.FlexForms.Application.Users.Queries;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Infrastructure.Security;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace GovUK.Dfe.FlexForms.Api.Security
{
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
            if (string.IsNullOrEmpty(issuer) ||
                !issuer.Contains("windows.net", StringComparison.OrdinalIgnoreCase))
            {
                return Array.Empty<Claim>();
            }

            var httpContext = httpContextAccessor.HttpContext;

            // AzureAd / Entra client-credentials callers are authorised via TenantAuthProvider
            // (IsServicePrincipal). They are not EA Users and do not belong in InternalServiceAuth.
            if (IsRegistryServicePrincipal(httpContext, principal))
            {
                RequestClaimEnrichmentGate.TryBegin(
                    httpContext,
                    RequestClaimEnrichmentGate.AzurePermissionsKey);
                return Array.Empty<Claim>();
            }

            if (!RequestClaimEnrichmentGate.TryBegin(
                    httpContext,
                    RequestClaimEnrichmentGate.AzurePermissionsKey))
            {
                return Array.Empty<Claim>();
            }

            var clientId = principal.FindFirst("appid")?.Value;

            if (string.IsNullOrEmpty(clientId))
            {
                logger.LogWarning("PermissionsClaimProvider() > Azure token had no appid");
                return Array.Empty<Claim>();
            }

            var dbUser = await (new GetUserByExternalProviderIdQueryObject(clientId))
                    .Apply(userRepo.Query().AsNoTracking())
                    .FirstOrDefaultAsync();

            if (dbUser is null)
            {
                logger.LogDebug(
                    "PermissionsClaimProvider() > No EA user mapped to Azure appid {ClientId}. " +
                    "Entra service callers do not need a Users row.",
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

            var claims = new List<Claim> { new(ClaimTypes.Role, dbUser.Role.Name) };

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

        private static bool IsRegistryServicePrincipal(HttpContext? httpContext, ClaimsPrincipal principal)
        {
            if (httpContext?.Items[AuthConstants.MatchedAuthProviderKey] is TenantAuthProvider
                {
                    IsServicePrincipal: true
                })
            {
                return true;
            }

            return principal.HasClaim(c =>
                c.Type == TenantAuthClaimTypes.IsService
                && string.Equals(c.Value, "true", StringComparison.OrdinalIgnoreCase));
        }
    }
}
