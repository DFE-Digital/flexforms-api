using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Exceptions;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Enums;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Events;
using GovUK.Dfe.CoreLibs.Messaging.MassTransit.Helpers;
using GovUK.Dfe.CoreLibs.Notifications;
using GovUK.Dfe.CoreLibs.Notifications.Interfaces;
using GovUK.Dfe.CoreLibs.Notifications.Models;
using GovUK.Dfe.FlexForms.Application.Applications.Commands;
using GovUK.Dfe.FlexForms.Application.Applications.EventHandlers;
using GovUK.Dfe.FlexForms.Application.Applications.QueryObjects;
using GovUK.Dfe.FlexForms.Application.Messaging;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using File = GovUK.Dfe.FlexForms.Domain.Entities.File;

namespace GovUK.Dfe.FlexForms.Application.Consumers;

/// <summary>
/// Consumer for file scan results from the virus scanner service.
/// Listens to the shared <c>file-scanner-results</c> subscription. Tenant context is set by
/// <c>TenantContextConsumeFilter</c> from inbound headers/metadata before this consumer runs.
/// Tenant, template, and requesting user are then matched in code — not via Service Bus SQL filters.
/// </summary>
public sealed class ScanResultConsumer(
    ILogger<ScanResultConsumer> logger,
    IEaRepository<File> fileRepository,
    ITenantContextAccessor tenantContextAccessor,
    ISender sender,
    INotificationService notificationService,
    INotificationSignalRService notificationSignalRService) : IConsumer<ScanResultEvent>
{
    public const string MalwareCategory = "malware-detection";

    public async Task Consume(ConsumeContext<ScanResultEvent> context)
    {
        var scanResult = context.Message;

        logger.LogInformation(
            "Received scan result - FileName: {FileName}, Status: {Status}, Outcome: {Outcome}",
            scanResult.FileName,
            scanResult.Status,
            scanResult.Outcome);

        var tenant = tenantContextAccessor.CurrentTenant;
        if (tenant is null)
        {
            logger.LogWarning(
                "Skipping scan result for {FileId}: tenant context was not resolved",
                scanResult.FileId);
            return;
        }

        var metadataTenantId = ScanEventRouting.GetMetadataGuid(scanResult.Metadata, ScanEventRouting.TenantIdMetadata);
        if (metadataTenantId is Guid stampedTenantId && stampedTenantId != tenant.Id)
        {
            logger.LogWarning(
                "Skipping scan result for {FileId}: metadata TenantId {MetadataTenantId} does not match resolved tenant {TenantId}",
                scanResult.FileId,
                stampedTenantId,
                tenant.Id);
            return;
        }

        if (InstanceIdentifierHelper.IsLocalEnvironment())
        {
            var messageInstanceId = ScanEventRouting.GetMetadata(
                scanResult.Metadata,
                ScanEventRouting.InstanceIdentifierMetadata);

            var localInstanceId = InstanceIdentifierHelper.GetInstanceIdentifier(tenant.Settings);

            if (!InstanceIdentifierHelper.IsMessageForThisInstance(messageInstanceId, localInstanceId))
            {
                logger.LogDebug(
                    "Message {FileId} not for this instance (MessageInstanceId: '{MessageInstanceId}', LocalInstanceId: '{LocalInstanceId}') - throwing exception to requeue for other consumers",
                    scanResult.FileId,
                    messageInstanceId ?? "none",
                    localInstanceId ?? "none");

                throw new MessageNotForThisInstanceException(
                    $"Message InstanceIdentifier '{messageInstanceId}' doesn't match local instance '{localInstanceId}'");
            }
        }

        try
        {
            if (string.IsNullOrEmpty(scanResult.FileName) || string.IsNullOrEmpty(scanResult.Path))
            {
                logger.LogWarning("ScanResultEvent has no FileUri, skipping");
                return;
            }

            var file = await new GetFileByPathAndFileNameQueryObject(scanResult.Path, scanResult.FileName)
                .Apply(fileRepository.Query()
                    .Include(f => f.UploadedByUser)
                    .Include(f => f.Application!)
                        .ThenInclude(a => a.TemplateVersion)
                    .AsNoTracking())
                .FirstOrDefaultAsync(context.CancellationToken);

            if (file == null)
            {
                logger.LogWarning(
                    "File not found in database - Path: {Path}, FileName: {FileName}",
                    scanResult.Path,
                    scanResult.FileName);
                return;
            }

            if (!MatchesRequestingUser(context, file))
            {
                logger.LogWarning(
                    "Skipping scan result for {FileId}: requesting user does not match file uploader {UploadedBy}",
                    scanResult.FileId,
                    file.UploadedBy.Value);
                return;
            }

            if (!MatchesTemplate(context, file))
            {
                logger.LogWarning(
                    "Skipping scan result for {FileId}: template does not match application template",
                    scanResult.FileId);
                return;
            }

            switch (scanResult.Outcome)
            {
                case VirusScanOutcome.Clean:
                    logger.LogInformation(
                        "File is clean - FileId: {FileId}, FileName: {FileName}",
                        file.Id!.Value,
                        scanResult.FileName);
                    break;

                case VirusScanOutcome.Infected:
                    logger.LogWarning(
                        "File is infected - FileId: {FileId}, FileName: {FileName}, Malware: {MalwareName}",
                        file.Id!.Value,
                        scanResult.FileName,
                        scanResult.MalwareName);

                    var deleteCommand = new DeleteInfectedFileCommand(file.Id);
                    var result = await sender.Send(deleteCommand, context.CancellationToken);

                    if (result.IsSuccess)
                    {
                        logger.LogWarning(
                            "Successfully deleted infected file - FileId: {FileId}",
                            file.Id.Value);

                        await NotifyUploaderOfInfectedFileAsync(
                            file,
                            scanResult.MalwareName,
                            context.CancellationToken);
                    }
                    else
                    {
                        logger.LogError(
                            "Failed to delete infected file - FileId: {FileId}, Error: {Error}",
                            file.Id.Value,
                            result.Error);
                    }
                    break;

                case VirusScanOutcome.Error:
                    logger.LogWarning(
                        "File scan result unknown - FileId: {FileId}, FileName: {FileName}, Message: {Message}",
                        file.Id!.Value,
                        scanResult.FileName,
                        scanResult.Message);
                    break;

                default:
                    logger.LogInformation(
                        "File scan status: {Status} - FileId: {FileId}, FileName: {FileName}",
                        scanResult.Status,
                        file.Id!.Value,
                        scanResult.FileName);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error processing scan result for file: {FileName}",
                scanResult.FileName);
            throw;
        }
    }

    private static bool MatchesRequestingUser(ConsumeContext<ScanResultEvent> context, File file)
    {
        var requestedUserId = ScanEventRouting.ResolveUserId(context.Headers, context.Message.Metadata);
        return requestedUserId is null || requestedUserId.Value == file.UploadedBy.Value;
    }

    private static bool MatchesTemplate(ConsumeContext<ScanResultEvent> context, File file)
    {
        var requestedTemplateId = ScanEventRouting.ResolveTemplateId(context.Headers, context.Message.Metadata);
        if (requestedTemplateId is null)
            return true;

        var fileTemplateId = file.Application?.TemplateVersion?.TemplateId.Value;
        return fileTemplateId == requestedTemplateId;
    }

    /// <summary>
    /// Stores the malware banner and pushes it on the API SignalR hub (the browser connects there).
    /// Done here because the Web consumer's S2S API calls are unauthenticated and must not own this.
    /// </summary>
    private async Task NotifyUploaderOfInfectedFileAsync(
        File file,
        string? malwareName,
        CancellationToken cancellationToken)
    {
        try
        {
            var email = file.UploadedByUser?.Email;
            if (string.IsNullOrWhiteSpace(email))
            {
                logger.LogWarning(
                    "No uploader email for infected file {FileId}; skipping malware notification.",
                    file.Id!.Value);
                return;
            }

            var displayName = string.IsNullOrWhiteSpace(file.OriginalFileName)
                ? file.FileName
                : file.OriginalFileName;
            var malwareLabel = string.IsNullOrWhiteSpace(malwareName) ? "unknown malware" : malwareName;
            var appContext = FileValidationRecordedEventHandler.ResolveNotificationContext(
                tenantContextAccessor.CurrentTenant);
            var templateId = file.Application?.TemplateVersion?.TemplateId.Value;

            var options = new NotificationOptions
            {
                Category = MalwareCategory,
                Context = NotificationContextHelper.BuildScopedContext(
                    appContext,
                    templateId?.ToString(),
                    MalwareCategory,
                    file.Id!.Value.ToString()),
                AutoDismiss = false,
                AutoDismissSeconds = 0,
                UserId = email,
                ReplaceExistingContext = true,
                Priority = NotificationPriority.High,
                Metadata = new Dictionary<string, object>
                {
                    ["fileId"] = file.Id.Value.ToString(),
                    ["fileName"] = displayName,
                    ["malwareName"] = malwareLabel,
                    ["applicationId"] = file.ApplicationId.Value.ToString(),
                    ["detectedAt"] = DateTimeOffset.UtcNow.ToString("o")
                }
            };

            var message =
                $"The selected file '{displayName}' contains a virus called [{malwareLabel}]. We have deleted the file. Upload a new one.";

            var stored = await notificationService.AddNotificationAsync(
                message,
                NotificationType.Error,
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
                UserId = stored.UserId ?? email,
                ActionUrl = stored.ActionUrl,
                Metadata = stored.Metadata ?? options.Metadata,
                Priority = stored.Priority
            };

            await notificationSignalRService.SendNotificationToUserAsync(email, dto, cancellationToken);

            logger.LogInformation(
                "Created malware notification for infected file {FileId} ({FileName}) user {UserEmail}",
                file.Id.Value,
                displayName,
                email);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to notify uploader about infected file {FileId}",
                file.Id?.Value);
        }
    }
}
