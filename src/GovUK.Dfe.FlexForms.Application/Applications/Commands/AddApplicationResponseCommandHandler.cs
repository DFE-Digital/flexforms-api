using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Common.Attributes;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using MediatR;
using GovUK.Dfe.FlexForms.Application.Common.Behaviours;
using GovUK.Dfe.FlexForms.Application.Services;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Applications.Commands;

[RateLimit(15, 30)]
public sealed record AddApplicationResponseCommand(
    Guid ApplicationId,
    string ResponseBody) : IRequest<Result<ApplicationResponseDto>>, IRateLimitedRequest;

public sealed class AddApplicationResponseCommandHandler(
    IAuthenticatedUserService authenticatedUserService,
    IPermissionCheckerService permissionCheckerService,
    IApplicationRepository applicationRepository,
    IApplicationResponseAppender responseAppender,
    IUserCacheInvalidator userCacheInvalidator,
    ITenantPermissionFilter tenantPermissionFilter,
    IMediator mediator) : IRequestHandler<AddApplicationResponseCommand, Result<ApplicationResponseDto>>
{
    public async Task<Result<ApplicationResponseDto>> Handle(
        AddApplicationResponseCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentUserResult = await authenticatedUserService.GetCurrentUserAsync(cancellationToken);
            if (!currentUserResult.IsSuccess)
            {
                return currentUserResult.ErrorCode switch
                {
                    DomainErrorCode.NotFound => Result<ApplicationResponseDto>.NotFound(currentUserResult.Error!),
                    DomainErrorCode.Forbidden => Result<ApplicationResponseDto>.Forbid(currentUserResult.Error!),
                    _ => Result<ApplicationResponseDto>.Failure(currentUserResult.Error!)
                };
            }

            var dbUser = currentUserResult.Value!;

            var canAccess = permissionCheckerService.HasPermission(ResourceType.Application, request.ApplicationId.ToString(), AccessType.Write);

            if (!canAccess)
                return Result<ApplicationResponseDto>.Forbid("User does not have permission to update this application");

            if (!await tenantPermissionFilter.ApplicationBelongsToCurrentTenantAsync(request.ApplicationId, cancellationToken))
            {
                return Result<ApplicationResponseDto>.NotFound("Application not found");
            }

            var applicationId = new ApplicationId(request.ApplicationId);
            var decodedResponseBody = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(request.ResponseBody));

            var append = responseAppender.Create(applicationId, decodedResponseBody, dbUser.Id!);

            var persisted = await applicationRepository.AppendResponseVersionAsync(
                applicationId,
                append.Response,
                append.Now,
                dbUser.Id!,
                cancellationToken);

            if (persisted is null)
                return Result<ApplicationResponseDto>.NotFound("Application not found");

            await mediator.Publish(append.DomainEvent, cancellationToken);

            await userCacheInvalidator.InvalidateForUserAsync(
                dbUser.Email,
                dbUser.ExternalProviderId,
                dbUser.Id!,
                cancellationToken);

            var (applicationReference, newResponse) = persisted.Value;

            return Result<ApplicationResponseDto>.Success(new ApplicationResponseDto(
                newResponse.Id!.Value,
                applicationReference,
                newResponse.ApplicationId.Value,
                newResponse.ResponseBody,
                newResponse.CreatedOn,
                newResponse.CreatedBy.Value));
        }
        catch (FormatException)
        {
            return Result<ApplicationResponseDto>.Failure("Invalid Base64 format for ResponseBody");
        }
        catch (Exception e)
        {
            return Result<ApplicationResponseDto>.Failure(e.Message);
        }
    }
}
