using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Security.Interfaces;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Api.Security;

/// <summary>
/// Azure AD service-principal claim provider: emits Template permission claims from
/// the unified <see cref="Permission"/> store.
/// </summary>
public class TemplatePermissionsClaimProvider(
    ILogger<TemplatePermissionsClaimProvider> logger,
    IEaRepository<User> userRepo) : ICustomClaimProvider
{
    public async Task<IEnumerable<Claim>> GetClaimsAsync(ClaimsPrincipal principal)
    {
        var issuer = principal.FindFirst(JwtRegisteredClaimNames.Iss)?.Value
                     ?? principal.FindFirst("iss")?.Value;
        if (string.IsNullOrEmpty(issuer) || !issuer.Contains("windows.net", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<Claim>();

        var clientId = principal.FindFirst("appid")?.Value;
        if (string.IsNullOrEmpty(clientId))
        {
            logger.LogWarning("TemplatePermissionsClaimProvider() > Azure token had no appid");
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
