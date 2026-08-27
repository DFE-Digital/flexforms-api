using System;
using System.Collections.Generic;
using System.IO;
using GovUK.Dfe.FlexForms.Application.Applications.QueryObjects;
using GovUK.Dfe.FlexForms.Application.Common.EventHandlers;
using GovUK.Dfe.FlexForms.Application.Messaging;
using GovUK.Dfe.FlexForms.Application.Options;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.CoreLibs.FileStorage.Interfaces;
using GovUK.Dfe.CoreLibs.Messaging.MassTransit.Helpers;
using GovUK.Dfe.CoreLibs.Messaging.MassTransit.Interfaces;
using GovUK.Dfe.CoreLibs.Messaging.MassTransit.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Tenancy;

namespace GovUK.Dfe.FlexForms.Application.Applications.EventHandlers;

public sealed class FileUploadedDomainEventHandler(
    ILogger<FileUploadedDomainEventHandler> logger,
    IEventPublisher publishEndpoint,
    ITenantContextAccessor tenantContextAccessor,
    IEnumerable<IAzureSpecificOperations> azureSpecificOperations,
    IApplicationRepository applicationRepository,
    IEaRepository<User> userRepository,
    IEventTriggerDispatcher eventTriggerDispatcher)
    : BaseEventHandler<Domain.Events.FileUploadedDomainEvent>(logger)
{
    protected override async Task HandleEvent(
        Domain.Events.FileUploadedDomainEvent notification, 
        CancellationToken cancellationToken)
    {
        var file = notification.File;

        var fileName = file.FileName;

        // FileURL is the Azure File Share path: {applicationReference}/{hashedFileName}
        var fileUrl = $"{file.Path}/{fileName}";

        // Local provider does not register IAzureSpecificOperations → file:// for local scanners.
        // Azure and Hybrid both register it → generate a real SAS (Hybrid stores on disk but
        // still uses Azure for SAS so the scanner can fetch the file).
        var azureOps = azureSpecificOperations.FirstOrDefault();
        string sasUri;
        bool isAzureFileShare;
        if (azureOps is null)
        {
            var localPath = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", fileUrl);
            sasUri = $"file:///{localPath.Replace("\\", "/")}";
            isAzureFileShare = false;
            logger.LogInformation(
                "Local FileStorage provider — using file URI for scan request: {SasUri}",
                sasUri);
        }
        else
        {
            sasUri = await azureOps.GenerateSasTokenAsync(
                fileUrl, DateTimeOffset.UtcNow.AddHours(1), "r", cancellationToken);
            isAzureFileShare = true;
            logger.LogInformation(
                "Generated Azure SAS for scan request (Hybrid or Azure FileStorage): {SasUri}",
                sasUri);
        }

        var tenant = tenantContextAccessor.CurrentTenant 
            ?? throw new InvalidOperationException("Tenant context is required to publish file upload events.");

        var templateId = await TryResolveTemplateIdAsync(file.ApplicationId, cancellationToken);
        var userId = file.UploadedBy.Value.ToString();

        // Create the integration event
        var fileUploadedEvent = new GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Events.ScanRequestedEvent(
            FileId:file.Id?.Value.ToString(),
            FileHash: notification.FileHash,
            Reference:file.ApplicationId.Value.ToString(),
            FileName: fileName,
            Path:file.Path,
            IsAzureFileShare: isAzureFileShare,
            FileUri: sasUri,
            ServiceName: $"extapi-{tenant.Name}",
            Metadata: new Dictionary<string, object>
            {
                { ScanEventRouting.TenantIdMetadata, tenant.Id.ToString() },
                { ScanEventRouting.TenantNameMetadata, tenant.Name },
                { ScanEventRouting.ApplicationNameMetadata, tenant.Settings["ApplicationName"] ?? tenant.Name },
                { ScanEventRouting.ReferenceMetadata, file.Application!.ApplicationReference },
                { ScanEventRouting.ApplicationIdMetadata, file.ApplicationId.Value },
                { ScanEventRouting.UserIdMetadata, file.UploadedBy.Value },
                { ScanEventRouting.OriginalFileNameMetadata, file.OriginalFileName },
                { ScanEventRouting.InstanceIdentifierMetadata, InstanceIdentifierHelper.GetInstanceIdentifier(tenant.Settings) ?? "" },
                { ScanEventRouting.TemplateIdMetadata, templateId ?? string.Empty },
            }
        );

        // TenantId/TenantName headers are stamped by TenantAwareEventPublisher.
        // Template and user are scan-specific so generic EventTriggers publishing is unchanged.
        var propertiesBuilder = AzureServiceBusMessagePropertiesBuilder
            .Create()
            .AddCustomProperty("serviceName", $"extapi-{tenant.Name}")
            .AddCustomProperty(ScanEventRouting.UserIdHeader, userId);

        if (!string.IsNullOrWhiteSpace(templateId))
            propertiesBuilder.AddCustomProperty(ScanEventRouting.TemplateIdHeader, templateId);

        var messageProperties = propertiesBuilder.Build();

        // Publish to Azure Service Bus via MassTransit — hardcoded platform guarantee.
        await publishEndpoint.PublishAsync(
            fileUploadedEvent, 
            messageProperties, 
            cancellationToken);

        logger.LogInformation(
            "Published ScanRequestedEvent to service bus - File: {FileName}",
            file.OriginalFileName);

        await DispatchConfiguredEventsAsync(notification, sasUri, cancellationToken);
    }

    /// <summary>
    /// Publishes the tenant's FileUploaded event bindings. Runs after the mandatory scan request
    /// so a mapping or configuration problem can never block virus scanning.
    /// </summary>
    private async Task DispatchConfiguredEventsAsync(
        Domain.Events.FileUploadedDomainEvent notification,
        string fileUri,
        CancellationToken cancellationToken)
    {
        var file = notification.File;

        try
        {
            var application = await new GetApplicationByIdQueryObject(file.ApplicationId)
                .Apply(applicationRepository.Query().AsNoTracking())
                .FirstOrDefaultAsync(cancellationToken);

            var templateId = application?.TemplateVersion?.TemplateId.Value.ToString();
            if (string.IsNullOrEmpty(templateId))
            {
                logger.LogWarning(
                    "Could not resolve a template for application {ApplicationId}; skipping FileUploaded event dispatch.",
                    file.ApplicationId.Value);
                return;
            }

            var latestResponse = await applicationRepository.GetLatestResponseAsync(
                file.ApplicationId,
                cancellationToken);

            var formData = ApplicationFormDataParser.Parse(latestResponse?.ResponseBody);

            var uploaderEmail = await ResolveUploaderEmailAsync(file, cancellationToken);

            var platformMetadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                [PlatformEventMetadataKeys.ApplicationId] = file.ApplicationId.Value.ToString(),
                [PlatformEventMetadataKeys.ApplicationReference] = application!.ApplicationReference,
                [PlatformEventMetadataKeys.FileId] = file.Id?.Value.ToString(),
                [PlatformEventMetadataKeys.FileName] = file.FileName,
                [PlatformEventMetadataKeys.OriginalFileName] = file.OriginalFileName,
                [PlatformEventMetadataKeys.FilePath] = file.Path,
                [PlatformEventMetadataKeys.FileUri] = fileUri,
                [PlatformEventMetadataKeys.FileHash] = notification.FileHash,
                [PlatformEventMetadataKeys.FileSize] = file.FileSize,
                [PlatformEventMetadataKeys.UploaderUserId] = file.UploadedBy.Value.ToString(),
                [PlatformEventMetadataKeys.UploaderEmail] = uploaderEmail,
                [PlatformEventMetadataKeys.UploadedOn] = file.UploadedOn
            };

            await eventTriggerDispatcher.DispatchAsync(
                EventTriggerType.FileUploaded,
                file.ApplicationId.Value,
                application.ApplicationReference,
                templateId,
                formData,
                platformMetadata,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error dispatching FileUploaded events for application {ApplicationId}",
                file.ApplicationId.Value);
        }
    }

    private async Task<string?> TryResolveTemplateIdAsync(
        Domain.ValueObjects.ApplicationId applicationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var application = await applicationRepository.Query()
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken);

            return application?.TemplateVersion?.TemplateId.Value.ToString();
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not resolve template for scan request metadata on application {ApplicationId}",
                applicationId.Value);
            return null;
        }
    }

    private async Task<string?> ResolveUploaderEmailAsync(
        Domain.Entities.File file,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(file.UploadedByUser?.Email))
            return file.UploadedByUser.Email;

        try
        {
            var user = await new GetUserByIdQueryObject(file.UploadedBy)
                .Apply(userRepository.Query().AsNoTracking())
                .FirstOrDefaultAsync(cancellationToken);

            return user?.Email;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not resolve uploader email for user {UserId}",
                file.UploadedBy.Value);
            return null;
        }
    }
}
