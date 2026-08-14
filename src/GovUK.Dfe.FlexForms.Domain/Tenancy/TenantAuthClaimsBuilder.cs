using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Services;

namespace GovUK.Dfe.FlexForms.Domain.Tenancy;

/// <summary>
/// Builds the baseline <see cref="Claim"/> set for a principal authenticated through a matched
/// <see cref="TenantAuthProvider"/>. Presentation-layer code (JWT bearer events, API key handler,
/// certificate handler) should use this type instead of assembling claim lists ad hoc.
/// </summary>
public static class TenantAuthClaimsBuilder
{
    /// <summary>
    /// Creates claims for <paramref name="provider"/> plus any optional transport-specific claims
    /// (for example certificate subject claims), then returns them as a new list.
    /// </summary>
    /// <param name="provider">The registry row that matched the incoming credential.</param>
    /// <param name="additionalClaims">Optional claims to append after the baseline (may be null).</param>
    public static IReadOnlyList<Claim> Build(TenantAuthProvider provider, IEnumerable<Claim>? additionalClaims = null)
    {
        var claims = new List<Claim>
        {
            new(TenantAuthClaimTypes.TenantId, provider.TenantId.ToString()),
            new(TenantAuthClaimTypes.AuthProvider, provider.Name),
            new(TenantAuthClaimTypes.IsService, provider.IsServicePrincipal ? "true" : "false")
        };

        if (provider.Roles is not null)
        {
            claims.AddRange(provider.Roles.Select(r => new Claim(ClaimTypes.Role, r)));

            if (provider.IsServicePrincipal
                && provider.Roles.Any(r => string.Equals(r, "FileValidation", StringComparison.OrdinalIgnoreCase)))
            {
                claims.Add(new Claim(
                    PermissionClaimEvaluator.PermissionClaimType,
                    PermissionClaimEvaluator.FormatPermissionClaim(
                        ResourceType.FileValidation,
                        PermissionConstants.AnyResourceKey,
                        AccessType.Write)));
            }
        }

        if (additionalClaims is not null)
        {
            claims.AddRange(additionalClaims);
        }

        return claims;
    }
}
