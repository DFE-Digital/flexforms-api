using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Applications.Mapping;
using GovUK.Dfe.FlexForms.Application.Applications.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using File = GovUK.Dfe.FlexForms.Domain.Entities.File;

namespace GovUK.Dfe.FlexForms.Application.Applications.Commands;

public sealed class RecordFileValidationResultCommandHandler(
    IEaRepository<File> fileRepository,
    IPermissionCheckerService permissionCheckerService,
    IUnitOfWork unitOfWork,
    ILogger<RecordFileValidationResultCommandHandler> logger)
    : IRequestHandler<RecordFileValidationResultCommand, Result<UploadDto>>
{
    public async Task<Result<UploadDto>> Handle(
        RecordFileValidationResultCommand request,
        CancellationToken cancellationToken)
    {
        var file = await new GetFileByIdQueryObject(new FileId(request.FileId))
            .Apply(fileRepository.Query()
                .Include(f => f.Application)
                .ThenInclude(a => a!.TemplateVersion))
            .FirstOrDefaultAsync(cancellationToken);

        if (file is null)
            return Result<UploadDto>.NotFound("File not found");

        var application = file.Application;
        if (application is null)
            return Result<UploadDto>.NotFound("Application not found");

        if (application.Status == ApplicationStatus.Submitted)
            return Result<UploadDto>.Conflict("Cannot record validation for a submitted application");

        var templateId = application.TemplateVersion?.TemplateId.Value.ToString();
        if (string.IsNullOrWhiteSpace(templateId)
            || !permissionCheckerService.CanWriteFileValidation(templateId))
        {
            return Result<UploadDto>.Forbid("Caller does not have permission to record file validation results");
        }

        try
        {
            file.RecordValidationResult(
                request.IsValid,
                request.Message,
                DateTime.UtcNow,
                request.Source);

            await unitOfWork.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Recorded file validation result {Status} for file {FileId} on application {ApplicationId}. CorrelationId: {CorrelationId}",
                file.ValidationStatus,
                file.Id!.Value,
                application.Id!.Value,
                request.CorrelationId);

            return Result<UploadDto>.Success(UploadDtoMapper.FromFile(file));
        }
        catch (InvalidOperationException ex)
        {
            return Result<UploadDto>.Conflict(ex.Message);
        }
    }
}
