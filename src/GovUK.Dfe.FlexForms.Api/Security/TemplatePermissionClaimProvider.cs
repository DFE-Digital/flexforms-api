using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Security.Interfaces;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Api.Security;

/// <summary>
/// Fallback Entra app-only claim provider: emits Template permission claims from
/// the unified <see cref="Permission"/> store when
/// <see cref="PermissionsClaimProvider"/> has not already enriched this request.
/// </summary>
public class TemplatePermissionsClaimProvider(
    ILogger<TemplatePermissionsClaimProvider> logger,
    IEaRepository<User> userRepo,
    IHttpContextAccessor httpContextAccessor) : ICustomClaimProvider
{
    public async Task<IEnumerable<Claim>> GetClaimsAsync(ClaimsPrincipal principal)
    {
        var issuer = principal.FindFirst(JwtRegisteredClaimNames.Iss)?.Value
                     ?? principal.FindFirst("iss")?.Value;
        if (!EntraClientIdentity.IsEntraIssuer(issuer) || !EntraClientIdentity.IsAppOnlyToken(principal))
            return Array.Empty<Claim>();

        // PermissionsClaimProvider already emits template grants for Entra tokens.
        if (httpContextAccessor.HttpContext?.Items.ContainsKey(
                RequestClaimEnrichmentGate.AzurePermissionsKey) == true)
        {
            return Array.Empty<Claim>();
        }

        if (!RequestClaimEnrichmentGate.TryBegin(
                httpContextAccessor.HttpContext,
                RequestClaimEnrichmentGate.AzurePermissionsKey))
        {
            return Array.Empty<Claim>();
        }

        var clientId = EntraClientIdentity.PickClientId(principal);
        if (string.IsNullOrEmpty(clientId))
        {
            logger.LogWarning("TemplatePermissionsClaimProvider() > Azure token had no azp/appid");
            return Array.Empty<Claim>();
        }

        var dbUser = await new GetUserByExternalProviderIdQueryObject(clientId)
            .Apply(userRepo.Query().AsNoTracking())
            .FirstOrDefaultAsync();

        if (dbUser?.Id is null)
            return Array.Empty<Claim>();

        var userWithPerms = await new GetUserWithAllPermissionsByUserIdQueryObject(dbUser.Id)
            .Apply(userRepo.Query().AsNoTracking())
            .FirstOrDefaultAsync();

        if (userWithPerms is null)
            return Array.Empty<Claim>();

        return UserTemplateAccess.GetTemplateGrants(userWithPerms)
            .Select(p => new Claim(
                "permission",
                $"{ResourceType.Template}:{p.ResourceKey}:{p.AccessType}"));
    }
}
