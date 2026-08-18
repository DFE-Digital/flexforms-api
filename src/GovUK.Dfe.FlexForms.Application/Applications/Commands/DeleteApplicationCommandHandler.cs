using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Applications.QueryObjects;
using GovUK.Dfe.FlexForms.Application.Common.Attributes;
using GovUK.Dfe.FlexForms.Application.Common.Behaviours;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Applications.Commands;

[RateLimit(1, 30)]
public sealed record DeleteApplicationCommand(Guid ApplicationId) : IRequest<Result<ApplicationDto>>, IRateLimitedRequest;

public sealed class DeleteApplicationCommandHandler(
    IEaRepository<Domain.Entities.Application> applicationRepo,
    IAuthenticatedUserService authenticatedUserService,
    IPermissionCheckerService permissionCheckerService,
    IUserCacheInvalidator userCacheInvalidator,
    IUnitOfWork unitOfWork) : IRequestHandler<DeleteApplicationCommand, Result<ApplicationDto>>
{
    public async Task<Result<ApplicationDto>> Handle(
        DeleteApplicationCommand request,
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
                return Result<ApplicationDto>.Forbid("User does not have permission to delete this application");

            var applicationId = new ApplicationId(request.ApplicationId);
            var application = await (new GetApplicationByIdQueryObject(applicationId))
                .Apply(applicationRepo.Query())
                .FirstOrDefaultAsync(cancellationToken);

            if (application is null)
                return Result<ApplicationDto>.NotFound("Application not found");

            var now = DateTime.UtcNow;
            application.Delete(now, dbUser.Id!, dbUser.Email, dbUser.Name); 

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
                DateDeleted = application.LastModifiedOn,
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