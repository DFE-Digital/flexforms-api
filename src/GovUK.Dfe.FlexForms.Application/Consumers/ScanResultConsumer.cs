using GovUK.Dfe.FlexForms.Application.Applications.Commands;
using GovUK.Dfe.FlexForms.Application.Applications.QueryObjects;
using GovUK.Dfe.FlexForms.Application.Messaging;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Exceptions;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Enums;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Events;
using GovUK.Dfe.CoreLibs.Messaging.MassTransit.Helpers;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
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
    ISender sender) : IConsumer<ScanResultEvent>
{
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
}
