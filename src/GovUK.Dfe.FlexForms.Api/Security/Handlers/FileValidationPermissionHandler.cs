using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;

namespace GovUK.Dfe.FlexForms.Api.Security.Handlers;

/// <summary>
/// Succeeds only for machine identities that have an explicit FileValidation Write grant.
/// Does not honour <see cref="PermissionClaimEvaluator.HasFullAdminAccess"/>.
/// </summary>
public sealed class FileValidationPermissionHandler(IHttpContextAccessor httpContextAccessor)
    : AuthorizationHandler<FileValidationPermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        FileValidationPermissionRequirement requirement)
    {
        if (!IsServicePrincipal(context, httpContextAccessor.HttpContext))
            return Task.CompletedTask;

        if (PermissionClaimEvaluator.CanWriteAnyFileValidation(context.User))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }

    private static bool IsServicePrincipal(AuthorizationHandlerContext context, HttpContext? http)
    {
        if (http is not null
            && http.Items.TryGetValue(AuthConstants.MatchedAuthProviderKey, out var providerObj)
            && providerObj is TenantAuthProvider provider
            && provider.IsServicePrincipal)
        {
            return true;
        }

        return context.User.HasClaim(c =>
            c.Type == TenantAuthClaimTypes.IsService
            && string.Equals(c.Value, "true", StringComparison.OrdinalIgnoreCase));
    }
}
