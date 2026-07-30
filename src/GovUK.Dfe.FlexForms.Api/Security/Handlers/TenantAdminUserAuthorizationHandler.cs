using System.Security.Claims;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace GovUK.Dfe.FlexForms.Api.Security.Handlers;

/// <summary>
/// Succeeds only for interactive user JWTs that carry the Admin role.
/// Fails for Entra client-credentials, API keys, mTLS, and any other
/// <c>is_service=true</c> / <see cref="TenantAuthProvider.IsServicePrincipal"/> identity,
/// even if AuthProviders stamp an Admin role onto the machine principal.
/// </summary>
public sealed class TenantAdminUserAuthorizationHandler(IHttpContextAccessor httpContextAccessor)
    : AuthorizationHandler<TenantAdminUserRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TenantAdminUserRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        if (IsServiceIdentity(context.User))
        {
            return Task.CompletedTask;
        }

        if (!context.User.IsInRole(RoleNames.SuperAdmin)
            && !context.User.IsInRole(RoleNames.Admin))
        {
            return Task.CompletedTask;
        }

        // Exchanged user JWTs always carry an email; machine principals usually do not.
        var email = context.User.FindFirstValue(ClaimTypes.Email)
            ?? context.User.FindFirstValue("email");
        if (string.IsNullOrWhiteSpace(email))
        {
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }

    private bool IsServiceIdentity(ClaimsPrincipal user)
    {
        if (user.HasClaim(c =>
                c.Type == TenantAuthClaimTypes.IsService
                && string.Equals(c.Value, "true", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var http = httpContextAccessor.HttpContext;
        if (http is not null
            && http.Items.TryGetValue(AuthConstants.MatchedAuthProviderKey, out var providerObj)
            && providerObj is TenantAuthProvider { IsServicePrincipal: true })
        {
            return true;
        }

        return false;
    }
}
