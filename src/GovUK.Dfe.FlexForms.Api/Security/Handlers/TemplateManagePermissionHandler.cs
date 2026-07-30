using GovUK.Dfe.FlexForms.Domain.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace GovUK.Dfe.FlexForms.Api.Security.Handlers;

/// <summary>
/// Succeeds when the caller can administer templates in the tenant.
/// </summary>
public sealed class TemplateManagePermissionHandler(IHttpContextAccessor accessor)
    : AuthorizationHandler<TemplateManagePermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TemplateManagePermissionRequirement requirement)
    {
        var templateId = accessor.HttpContext?.Request.RouteValues["templateId"]?.ToString();
        if ((!string.IsNullOrWhiteSpace(templateId) && PermissionClaimEvaluator.CanManageTemplate(context.User, templateId))
            || PermissionClaimEvaluator.CanManageTemplates(context.User))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
