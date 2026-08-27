using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace GovUK.Dfe.FlexForms.Api.Security.Handlers;

/// <summary>
/// Succeeds when the caller can administer templates in the tenant.
/// </summary>
public sealed class TemplateManagePermissionHandler(IHttpContextAccessor accessor)
    : AuthorizationHandler<TemplateManagePermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TemplateManagePermissionRequirement requirement)
    {
        var templateIdRaw = accessor.HttpContext?.Request.RouteValues["templateId"]?.ToString();
        if (!string.IsNullOrWhiteSpace(templateIdRaw) && Guid.TryParse(templateIdRaw, out var templateGuid))
        {
            var tenantTemplateResolver = accessor.HttpContext?.RequestServices
                .GetService(typeof(ITenantTemplateResolver)) as ITenantTemplateResolver;
            if (tenantTemplateResolver is null
                || !await tenantTemplateResolver.IsTemplateInCurrentTenantAsync(
                    new TemplateId(templateGuid),
                    CancellationToken.None))
            {
                return;
            }

            if (PermissionClaimEvaluator.CanManageTemplate(context.User, templateIdRaw)
                || PermissionClaimEvaluator.CanManageTemplates(context.User))
            {
                context.Succeed(requirement);
            }

            return;
        }

        if (PermissionClaimEvaluator.CanManageTemplates(context.User))
            context.Succeed(requirement);
    }
}
