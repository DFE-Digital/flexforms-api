using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Applications.QueryObjects;
using GovUK.Dfe.FlexForms.Application.Common.Attributes;
using GovUK.Dfe.FlexForms.Application.Common.Behaviours;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;
using File = GovUK.Dfe.FlexForms.Domain.Entities.File;

namespace GovUK.Dfe.FlexForms.Application.Applications.Commands;

[RateLimit(1, 30)]
public sealed record SubmitApplicationCommand(Guid ApplicationId) : IRequest<Result<ApplicationDto>>, IRateLimitedRequest;

public sealed class SubmitApplicationCommandHandler(
    IEaRepository<Domain.Entities.Application> applicationRepo,
    IEaRepository<File> fileRepository,
    IAuthenticatedUserService authenticatedUserService,
    IPermissionCheckerService permissionCheckerService,
    IFileValidationModeResolver fileValidationModeResolver,
    IApplicationFileValidationPolicy fileValidationPolicy,
    IUserCacheInvalidator userCacheInvalidator,
    IUnitOfWork unitOfWork) : IRequestHandler<SubmitApplicationCommand, Result<ApplicationDto>>
{
    public async Task<Result<ApplicationDto>> Handle(
        SubmitApplicationCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentUserResult = await authenticatedUserService.GetCurrentUserAsync(cancellationToken);
            if (!currentUserResult.IsSuccess)
            {
                return currentUserResult.ErrorCode switch
                {
                    DomainErrorCode.NotFound => Result<ApplicationDto>.NotFound(currentUserResult.Error!),
                    DomainErrorCode.Forbidden => Result<ApplicationDto>.Forbid(currentUserResult.Error!),
                    _ => Result<ApplicationDto>.Failure(currentUserResult.Error!)
                };
            }

            var dbUser = currentUserResult.Value!;

            var canAccess = permissionCheckerService.HasPermission(
                ResourceType.Application,
                request.ApplicationId.ToString(),
                AccessType.Write);

            if (!canAccess)
                return Result<ApplicationDto>.Forbid("User does not have permission to submit this application");

            var applicationId = new ApplicationId(request.ApplicationId);
            var application = await new GetApplicationByIdQueryObject(applicationId)
                .Apply(applicationRepo.Query())
                .FirstOrDefaultAsync(cancellationToken);

            if (application is null)
                return Result<ApplicationDto>.NotFound("Application not found");

            if (application.CreatedBy != dbUser.Id)
                return Result<ApplicationDto>.Forbid("Only the user who created the application can submit it");

            var files = await new GetFilesByApplicationIdQueryObject(applicationId)
                .Apply(fileRepository.Query())
                .ToListAsync(cancellationToken);

            var mode = fileValidationModeResolver.Resolve(application.TemplateVersion?.TemplateId.Value);
            var gate = fileValidationPolicy.Evaluate(mode, files);
            if (!gate.CanSubmit)
                return Result<ApplicationDto>.Validation(gate.ToErrorMessage());

            var now = DateTime.UtcNow;
            application.Submit(now, dbUser.Id!, dbUser.Email, dbUser.Name);

            await unitOfWork.CommitAsync(cancellationToken);

            await userCacheInvalidator.InvalidateForUserAsync(
                dbUser.Email,
                dbUser.ExternalProviderId,
                dbUser.Id!,
                cancellationToken);

            return Result<ApplicationDto>.Success(new ApplicationDto
            {
                ApplicationId = application.Id!.Value,
                ApplicationReference = application.ApplicationReference,
                TemplateVersionId = application.TemplateVersionId.Value,
                TemplateName = application.TemplateVersion?.Template?.Name ?? string.Empty,
                Status = application.Status,
                DateCreated = application.CreatedOn,
                DateSubmitted = application.LastModifiedOn,
                LatestResponse = null,
                TemplateSchema = null
            });
        }
        catch (Exception e)
        {
            return Result<ApplicationDto>.Failure(e.Message);
        }
    }
}
