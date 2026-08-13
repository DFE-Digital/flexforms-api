using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Applications.QueryObjects;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;
using File = GovUK.Dfe.FlexForms.Domain.Entities.File;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Applications.Queries;

public sealed record GetApplicationFileValidationGateQuery(Guid ApplicationId)
    : IRequest<Result<FileValidationGateDto>>;

public sealed class GetApplicationFileValidationGateQueryHandler(
    IEaRepository<Domain.Entities.Application> applicationRepository,
    IEaRepository<File> fileRepository,
    IPermissionCheckerService permissionCheckerService,
    IFileValidationModeResolver fileValidationModeResolver,
    IApplicationFileValidationPolicy fileValidationPolicy)
    : IRequestHandler<GetApplicationFileValidationGateQuery, Result<FileValidationGateDto>>
{
    public async Task<Result<FileValidationGateDto>> Handle(
        GetApplicationFileValidationGateQuery request,
        CancellationToken cancellationToken)
    {
        if (!permissionCheckerService.HasPermission(
                ResourceType.Application,
                request.ApplicationId.ToString(),
                AccessType.Read)
            && !permissionCheckerService.HasPermission(
                ResourceType.ApplicationFiles,
                request.ApplicationId.ToString(),
                AccessType.Read))
        {
            return Result<FileValidationGateDto>.Forbid("User does not have permission to read this application");
        }

        var applicationId = new ApplicationId(request.ApplicationId);
        var application = await new GetApplicationByIdQueryObject(applicationId)
            .Apply(applicationRepository.Query().AsNoTracking())
            .FirstOrDefaultAsync(cancellationToken);

        if (application is null)
            return Result<FileValidationGateDto>.NotFound("Application not found");

        var files = await new GetFilesByApplicationIdQueryObject(applicationId)
            .Apply(fileRepository.Query().AsNoTracking())
            .ToListAsync(cancellationToken);

        var mode = fileValidationModeResolver.Resolve(application.TemplateVersion?.TemplateId.Value);
        var gate = fileValidationPolicy.Evaluate(mode, files);

        return Result<FileValidationGateDto>.Success(new FileValidationGateDto
        {
            Mode = mode,
            CanSubmit = gate.CanSubmit,
            BlockingFiles = gate.BlockingFiles.Select(file => new FileValidationBlockDto
            {
                FileId = file.Id!.Value,
                OriginalFileName = file.OriginalFileName,
                ValidationStatus = file.ValidationStatus,
                ValidationMessage = file.ValidationMessage
            }).ToList()
        });
    }
}
