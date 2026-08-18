using GovUK.Dfe.FlexForms.Domain.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;

namespace GovUK.Dfe.FlexForms.Api.Security.Handlers
{
    /// <summary>
    /// Authorization handler that checks notifications permission claims for a specific user resource.
    /// </summary>
    public sealed class NotificationsPermissionHandler(IHttpContextAccessor accessor)
        : AuthorizationHandler<NotificationsPermissionRequirement>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            NotificationsPermissionRequirement requirement)
        {
            if (PermissionClaimEvaluator.HasFullAdminAccess(context.User))
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }

            var httpContext = accessor.HttpContext;
            var resourceKey = httpContext?.Request.RouteValues["email"]?.ToString();

            if (string.IsNullOrWhiteSpace(resourceKey))
                resourceKey = context.User.FindFirstValue(ClaimTypes.Email);

            if (string.IsNullOrWhiteSpace(resourceKey))
                resourceKey = context.User.FindFirst("appid")?.Value
                              ?? context.User.FindFirst("azp")?.Value;

            if (string.IsNullOrWhiteSpace(resourceKey))
                return Task.CompletedTask;

            var expectedAccess = Enum.TryParse<AccessType>(requirement.Action, ignoreCase: true, out var accessType)
                ? accessType
                : (AccessType?)null;

            if (expectedAccess is null)
                return Task.CompletedTask;

            if (PermissionClaimEvaluator.HasPermissionClaim(
                    context.User,
                    ResourceType.Notifications,
                    resourceKey,
                    expectedAccess.Value))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
