using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Common.Attributes;
using GovUK.Dfe.FlexForms.Application.Common.Behaviours;
using MediatR;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Notifications.Interfaces;
using GovUK.Dfe.CoreLibs.Notifications.Models;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Notifications.Commands;

[RateLimit(5, 30)]
public sealed record AddNotificationCommand(
    string Message,
    NotificationType Type,
    string? Category = null,
    string? Context = null,
    bool? AutoDismiss = null,
    int? AutoDismissSeconds = null,
    string? ActionUrl = null,
    Dictionary<string, object>? Metadata = null,
    NotificationPriority? Priority = null,
    bool? ReplaceExistingContext = null,
    UserId? ToUserId = null) : IRequest<Result<NotificationDto>>, IRateLimitedRequest;

public sealed class AddNotificationCommandHandler(
    INotificationService notificationService,
    IPermissionCheckerService permissionCheckerService,
    INotificationSignalRService notificationSignalRService,
    IHttpContextAccessor httpContextAccessor,
    IEaRepository<User> userRepo)
    : IRequestHandler<AddNotificationCommand, Result<NotificationDto>>
{
    public async Task<Result<NotificationDto>> Handle(
        AddNotificationCommand request, 
        CancellationToken cancellationToken)
    {
        try
        {
            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext?.User is not ClaimsPrincipal user || !user.Identity?.IsAuthenticated == true)
                return Result<NotificationDto>.Forbid("Not authenticated");

            var principalId = user.FindFirstValue(ClaimTypes.Email);
            
            if (string.IsNullOrEmpty(principalId))
                principalId = user.FindFirstValue("appid") ?? user.FindFirstValue("azp");

            if (string.IsNullOrEmpty(principalId))
                return Result<NotificationDto>.Forbid("No user identifier");

            var canAccess = permissionCheckerService.HasPermission(ResourceType.Notifications, principalId, AccessType.Write);

            if (!canAccess)
                return Result<NotificationDto>.Forbid("User does not have permission to create notifications");

            User? toUser = null;

            if (request.ToUserId != null && CanTargetOtherUser(user))
            {
                var users = userRepo.Query();
                if (users is not null)
                {
                    toUser = await (new GetUserByIdQueryObject(request.ToUserId))
                        .Apply(users.AsNoTracking())
                        .FirstOrDefaultAsync(cancellationToken);
                }
            }

            var options = new NotificationOptions
            {
                Category = request.Category,
                Context = request.Context,
                AutoDismiss = request.AutoDismiss ?? true,
                AutoDismissSeconds = request.AutoDismissSeconds ?? 5,
                UserId = toUser?.Email ?? principalId,
                ActionUrl = request.ActionUrl,
                Metadata = request.Metadata,
                Priority = request.Priority ?? NotificationPriority.Normal,
                ReplaceExistingContext = request.ReplaceExistingContext ?? true
            };

            var notification = await notificationService.AddNotificationAsync(
                request.Message, 
                request.Type, 
                options, 
                cancellationToken);

            var dto = new NotificationDto
            {
                Id = notification.Id,
                Message = notification.Message,
                Type = notification.Type,
                Category = notification.Category,
                Context = notification.Context,
                IsRead = notification.IsRead,
                CreatedAt = notification.CreatedAt,
                AutoDismiss = notification.AutoDismiss,
                AutoDismissSeconds = notification.AutoDismissSeconds,
                UserId = notification.UserId,
                ActionUrl = notification.ActionUrl,
                Metadata = notification.Metadata,
                Priority = notification.Priority
            };

            // Send real-time notification via SignalR
            await notificationSignalRService.SendNotificationToUserAsync(toUser?.Email ?? principalId, dto, cancellationToken);

            return Result<NotificationDto>.Success(dto);
        }
        catch (Exception ex)
        {
            return Result<NotificationDto>.Failure(ex.Message);
        }
    }

    private bool CanTargetOtherUser(ClaimsPrincipal caller) =>
        permissionCheckerService.IsAdmin() || IsServiceIdentity(caller);

    private static bool IsServiceIdentity(ClaimsPrincipal caller)
    {
        if (caller.HasClaim(c =>
                c.Type == TenantAuthClaimTypes.IsService
                && string.Equals(c.Value, "true", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var email = caller.FindFirstValue(ClaimTypes.Email);
        if (!string.IsNullOrEmpty(email))
            return false;

        return !string.IsNullOrEmpty(caller.FindFirstValue("appid"))
            || !string.IsNullOrEmpty(caller.FindFirstValue("azp"));
    }
}
