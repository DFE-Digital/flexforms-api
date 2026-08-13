using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Notifications.Interfaces;
using GovUK.Dfe.CoreLibs.Notifications.Models;
using GovUK.Dfe.FlexForms.Application.Common.EventHandlers;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Events;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.Applications.EventHandlers;

/// <summary>
/// Pushes a GOV.UK notification (and SignalR) to the uploader when a tenant function
/// records a file validation result — same path as the malware / infected-file delete banner.
/// </summary>
public sealed class FileValidationRecordedEventHandler(
    ILogger<FileValidationRecordedEventHandler> logger,
    IEaRepository<User> userRepository,
    INotificationService notificationService,
    INotificationSignalRService notificationSignalRService,
    ITenantContextAccessor tenantContextAccessor)
    : BaseEventHandler<FileValidationRecordedEvent>(logger)
{
    public const string Category = "file-validation";

    protected override async Task HandleEvent(
        FileValidationRecordedEvent notification,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await userRepository.Query()
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == notification.UploadedBy, cancellationToken);

            if (user is null || string.IsNullOrWhiteSpace(user.Email))
            {
                logger.LogWarning(
                    "No uploader email for file {FileId}; skipping file-validation notification.",
                    notification.FileId.Value);
                return;
            }

            var failed = notification.Status == FileValidationStatus.Failed;
            var message = failed
                ? BuildFailedMessage(notification.OriginalFileName, notification.Message)
                : $"The file '{notification.OriginalFileName}' has been validated.";

            var options = new NotificationOptions
            {
                Category = Category,
                Context = ResolveNotificationContext(tenantContextAccessor.CurrentTenant),
                AutoDismiss = !failed,
                AutoDismissSeconds = failed ? 0 : 8,
                UserId = user.Email,
                ReplaceExistingContext = true,
                Priority = failed ? NotificationPriority.High : NotificationPriority.Normal,
                Metadata = new Dictionary<string, object>
                {
                    ["fileId"] = notification.FileId.Value.ToString(),
                    ["fileName"] = notification.OriginalFileName,
                    ["status"] = notification.Status.ToString(),
                    ["applicationId"] = notification.ApplicationId.Value.ToString(),
                    ["message"] = notification.Message ?? string.Empty
                }
            };

            var stored = await notificationService.AddNotificationAsync(
                message,
                failed ? NotificationType.Error : NotificationType.Success,
                options,
                cancellationToken);

            var dto = new NotificationDto
            {
                Id = stored.Id,
                Message = stored.Message,
                Type = stored.Type,
                Category = stored.Category ?? options.Category,
                Context = stored.Context ?? options.Context,
                IsRead = stored.IsRead,
                CreatedAt = stored.CreatedAt,
                AutoDismiss = stored.AutoDismiss,
                AutoDismissSeconds = stored.AutoDismissSeconds,
                UserId = stored.UserId ?? user.Email,
                ActionUrl = stored.ActionUrl,
                Metadata = stored.Metadata ?? options.Metadata,
                Priority = stored.Priority
            };

            await notificationSignalRService.SendNotificationToUserAsync(user.Email, dto, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to notify uploader about file validation for file {FileId}",
                notification.FileId.Value);
        }
    }

    private static string BuildFailedMessage(string fileName, string? detail)
    {
        var prefix = $"We could not validate '{fileName}'.";
        return string.IsNullOrWhiteSpace(detail) ? prefix : $"{prefix} {detail.Trim()}";
    }

    /// <summary>
    /// Web list/badge queries filter by this context (ApplicationName, else TenantName).
    /// File-delete and malware notifications use the same value.
    /// </summary>
    internal static string ResolveNotificationContext(TenantConfiguration? tenant)
    {
        if (tenant is null)
            return "platform";

        var applicationName = tenant.Settings["ApplicationName"];
        return string.IsNullOrWhiteSpace(applicationName) ? tenant.Name : applicationName;
    }
}
