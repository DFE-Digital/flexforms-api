using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace GovUK.Dfe.FlexForms.Api.Security.Handlers
{
    public sealed class TemplatePermissionHandler(IHttpContextAccessor accessor)
        : AuthorizationHandler<TemplatePermissionRequirement>
    {
        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            TemplatePermissionRequirement requirement)
        {
            var templateIdRaw = accessor.HttpContext?.Request.RouteValues["templateId"]?.ToString();
            TemplateId? templateId = null;
            if (!string.IsNullOrWhiteSpace(templateIdRaw) && Guid.TryParse(templateIdRaw, out var templateGuid))
                templateId = new TemplateId(templateGuid);

            // When a specific template is targeted, it must belong to the current tenant —
            // including for Tenant Admin (otherwise Admin can hit foreign template IDs).
            if (templateId is not null)
            {
                var tenantTemplateResolver = accessor.HttpContext?.RequestServices
                    .GetService(typeof(ITenantTemplateResolver)) as ITenantTemplateResolver;
                if (tenantTemplateResolver is null
                    || !await tenantTemplateResolver.IsTemplateInCurrentTenantAsync(
                        templateId,
                        CancellationToken.None))
                {
                    return;
                }
            }

            if (PermissionClaimEvaluator.HasTenantAdminAccess(context.User))
            {
                context.Succeed(requirement);
                return;
            }

            // Caseworker-style roles have Application:Any:Read but not Template:*:Read.
            // They still need custom status labels (and other template reads) on /applications.
            if (requirement.Action.Equals("Read", StringComparison.OrdinalIgnoreCase)
                && PermissionClaimEvaluator.CanReadAllApplications(context.User))
            {
                context.Succeed(requirement);
                return;
            }

            if (templateId is not null
                && PermissionClaimEvaluator.CanManageTemplate(context.User, templateId.Value.ToString()))
            {
                context.Succeed(requirement);
                return;
            }

            // First check if the user has any template permission for the requested action
            var hasAnyTemplatePermission = context.User.Claims.Any(c =>
                c.Type == "permission" &&
                c.Value.StartsWith("Template:", StringComparison.OrdinalIgnoreCase) &&
                c.Value.EndsWith($":{requirement.Action}", StringComparison.OrdinalIgnoreCase));

            if (!hasAnyTemplatePermission)
                return;

            // Then check for specific template permission if templateId is provided
            if (templateId is null)
            {
                context.Succeed(requirement);
                return;
            }

            var expected = $"Template:{templateId.Value}:{requirement.Action}";
            var hasSpecificClaim = context.User.Claims.Any(c =>
                c.Type == "permission" &&
                string.Equals(c.Value, expected, StringComparison.OrdinalIgnoreCase));

            if (hasSpecificClaim)
                context.Succeed(requirement);
        }
    }
}
