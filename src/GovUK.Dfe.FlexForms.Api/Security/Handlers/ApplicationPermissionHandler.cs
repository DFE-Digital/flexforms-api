using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Services;
using Microsoft.AspNetCore.Authorization;

namespace GovUK.Dfe.FlexForms.Api.Security.Handlers
{
    /// <summary>
    /// Authorization handler that checks user permission claims for a specific application resource.
    /// Tenant membership for the application is enforced in application handlers as NotFound (404),
    /// so missing or cross-tenant IDs do not surface as 403 from this gate.
    /// </summary>
    public sealed class ApplicationPermissionHandler(
        IHttpContextAccessor accessor)
        : AuthorizationHandler<ApplicationPermissionRequirement>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            ApplicationPermissionRequirement requirement)
        {
            var applicationId = accessor.HttpContext?.Request.RouteValues["applicationId"]?.ToString();
            if (string.IsNullOrWhiteSpace(applicationId))
                return Task.CompletedTask;

            if (PermissionClaimEvaluator.HasFullAdminAccess(context.User))
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }

            var hasAccess = requirement.Action.Equals(AccessType.Read.ToString(), StringComparison.OrdinalIgnoreCase)
                ? PermissionClaimEvaluator.CanReadApplication(context.User, applicationId)
                : PermissionClaimEvaluator.CanWriteApplication(context.User, applicationId);

            if (hasAccess)
                context.Succeed(requirement);

            return Task.CompletedTask;
        }
    }
}
