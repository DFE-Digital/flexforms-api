using GovUK.Dfe.FlexForms.Domain.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace GovUK.Dfe.FlexForms.Api.Security.Handlers
{
    public sealed class TemplatePermissionHandler(IHttpContextAccessor accessor)
        : AuthorizationHandler<TemplatePermissionRequirement>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            TemplatePermissionRequirement requirement)
        {
            if (PermissionClaimEvaluator.HasTenantAdminAccess(context.User))
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }

            var templateId = accessor.HttpContext?.Request.RouteValues["templateId"]?.ToString();
            if (!string.IsNullOrWhiteSpace(templateId)
                && PermissionClaimEvaluator.CanManageTemplate(context.User, templateId))
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }

            // First check if the user has any template permission for the requested action
            var hasAnyTemplatePermission = context.User.Claims.Any(c =>
                c.Type == "permission" &&
                c.Value.StartsWith("Template:", StringComparison.OrdinalIgnoreCase) &&
                c.Value.EndsWith($":{requirement.Action}", StringComparison.OrdinalIgnoreCase));

            if (!hasAnyTemplatePermission)
                return Task.CompletedTask;

            // Then check for specific template permission if templateId is provided
            if (string.IsNullOrWhiteSpace(templateId))
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }

            var expected = $"Template:{templateId}:{requirement.Action}";
            var hasSpecificClaim = context.User.Claims.Any(c =>
                c.Type == "permission" &&
                string.Equals(c.Value, expected, StringComparison.OrdinalIgnoreCase));

            if (hasSpecificClaim)
                context.Succeed(requirement);

            return Task.CompletedTask;
        }
    }
}
