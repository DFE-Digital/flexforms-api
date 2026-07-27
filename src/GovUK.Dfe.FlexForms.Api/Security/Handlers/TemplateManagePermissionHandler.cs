using GovUK.Dfe.FlexForms.Domain.Services;
using Microsoft.AspNetCore.Authorization;

namespace GovUK.Dfe.FlexForms.Api.Security.Handlers;

/// <summary>
/// Succeeds when the caller can administer templates in the tenant.
/// </summary>
public sealed class TemplateManagePermissionHandler
    : AuthorizationHandler<TemplateManagePermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TemplateManagePermissionRequirement requirement)
    {
        if (PermissionClaimEvaluator.CanManageTemplates(context.User))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
