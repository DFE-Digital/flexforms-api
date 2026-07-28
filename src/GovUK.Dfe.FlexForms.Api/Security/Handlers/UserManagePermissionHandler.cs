using GovUK.Dfe.FlexForms.Domain.Services;
using Microsoft.AspNetCore.Authorization;

namespace GovUK.Dfe.FlexForms.Api.Security.Handlers;

/// <summary>
/// Succeeds when the caller can administer users in the tenant.
/// </summary>
public sealed class UserManagePermissionHandler
    : AuthorizationHandler<UserManagePermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        UserManagePermissionRequirement requirement)
    {
        if (PermissionClaimEvaluator.CanManageUsers(context.User))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
