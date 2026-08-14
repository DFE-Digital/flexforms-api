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

[RateLimit(10, 60)]
public sealed record MarkNotificationAsReadCommand(string NotificationId) : IRequest<Result<bool>>, IRateLimitedRequest;

public sealed class MarkNotificationAsReadCommandHandler(
    INotificationService notificationService,
    IPermissionCheckerService permissionCheckerService,
    INotificationSignalRService notificationSignalRService,
    IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<MarkNotificationAsReadCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        MarkNotificationAsReadCommand request, 
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

            var canAccess = permissionCheckerService.HasPermission(ResourceType.Notifications, principalId, AccessType.Write);
            if (!canAccess)
                return Result<bool>.Forbid("User does not have permission to modify notifications");

            await notificationService.MarkAsReadAsync(request.NotificationId, principalId, cancellationToken);

            // Send real-time notification update via SignalR
            await notificationSignalRService.SendNotificationListRefreshToUserAsync(principalId, cancellationToken);

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(ex.Message);
        }
    }
}
