using System.Security.Claims;
using GovUK.Dfe.FlexForms.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;

namespace GovUK.Dfe.FlexForms.Api.Security.Handlers;

/// <summary>
/// Succeeds when the authenticated principal has the <see cref="PlatformConstants.PrismReadAppRole"/> app role.
/// </summary>
public sealed class PlatformPrismReadRoleAuthorizationHandler
    : AuthorizationHandler<PlatformPrismReadRoleRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PlatformPrismReadRoleRequirement requirement)
    {
        if (context.User.Claims.Any(c =>
                IsRoleClaimType(c.Type)
                && string.Equals(c.Value, PlatformConstants.PrismReadAppRole, StringComparison.OrdinalIgnoreCase)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    private static bool IsRoleClaimType(string claimType) =>
        claimType.Equals(ClaimTypes.Role, StringComparison.OrdinalIgnoreCase)
        || claimType.Equals("roles", StringComparison.OrdinalIgnoreCase);
}
