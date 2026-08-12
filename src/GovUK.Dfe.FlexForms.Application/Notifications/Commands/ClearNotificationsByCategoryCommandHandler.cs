using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Common.Attributes;
using GovUK.Dfe.FlexForms.Application.Common.Behaviours;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.CoreLibs.Notifications.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Application.Notifications.Commands;

[RateLimit(5, 30)]
public sealed record ClearNotificationsByCategoryCommand(string Category) : IRequest<Result<bool>>, IRateLimitedRequest;

public sealed class ClearNotificationsByCategoryCommandHandler(
    INotificationService notificationService,
    IPermissionCheckerService permissionCheckerService,
    INotificationSignalRService notificationSignalRService,
    IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<ClearNotificationsByCategoryCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        ClearNotificationsByCategoryCommand request, 
        CancellationToken cancellationToken)
    {
        try
        {
            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext?.User is not ClaimsPrincipal user || !user.Identity?.IsAuthenticated == true)
                return Result<bool>.Forbid("Not authenticated");

            var principalId = user.FindFirstValue(ClaimTypes.Email);
            
            if (string.IsNullOrEmpty(principalId))
                principalId = user.FindFirstValue("appid") ?? user.FindFirstValue("azp");

            if (string.IsNullOrEmpty(principalId))
                return Result<bool>.Forbid("No user identifier");

            var canAccess = permissionCheckerService.HasPermission(ResourceType.Notifications, principalId, AccessType.Delete);
            if (!canAccess)
                return Result<bool>.Forbid("User does not have permission to delete notifications");

            await notificationService.ClearNotificationsByCategoryAsync(request.Category,principalId, cancellationToken);

            // Send real-time notification list refresh via SignalR
            await notificationSignalRService.SendNotificationListRefreshToUserAsync(principalId, cancellationToken);

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(ex.Message);
        }
    }
}
