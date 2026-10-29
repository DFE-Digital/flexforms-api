using FluentValidation;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Applications.QueryObjects;
using GovUK.Dfe.FlexForms.Application.Common.Attributes;
using GovUK.Dfe.FlexForms.Application.Common.Behaviours;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Applications.Commands;

[RateLimit(5, 30)]
public sealed record ChangeApplicationOwnerCommand(Guid ApplicationId, string Email)
    : IRequest<Result<ChangeApplicationOwnerResponse>>, IRateLimitedRequest;

public sealed class ChangeApplicationOwnerCommandHandler(
    IEaRepository<Domain.Entities.Application> applicationRepo,
    IEaRepository<User> userRepo,
    IAuthenticatedUserService authenticatedUserService,
    IPermissionCheckerService permissionCheckerService,
    ITenantTemplateResolver tenantTemplateResolver,
    ITenantContextAccessor tenantContextAccessor,
    ITenantMembershipService tenantMembershipService,
    IUserFactory userFactory,
    IUserCacheInvalidator userCacheInvalidator,
    IProjectionEventPublisher projectionEventPublisher,
    ITenantAccessAuditWriter accessAuditWriter,
    IUnitOfWork unitOfWork) : IRequestHandler<ChangeApplicationOwnerCommand, Result<ChangeApplicationOwnerResponse>>
{
    public async Task<Result<ChangeApplicationOwnerResponse>> Handle(
        ChangeApplicationOwnerCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!permissionCheckerService.IsAdmin())
                return Result<ChangeApplicationOwnerResponse>.Forbid("Only a tenant admin can change the lead applicant");

            var currentUserResult = await authenticatedUserService.GetCurrentUserAsync(cancellationToken);
            if (!currentUserResult.IsSuccess)
            {
                return currentUserResult.ErrorCode switch
                {
                    DomainErrorCode.NotFound => Result<ChangeApplicationOwnerResponse>.NotFound(currentUserResult.Error!),
                    DomainErrorCode.Forbidden => Result<ChangeApplicationOwnerResponse>.Forbid(currentUserResult.Error!),
                    _ => Result<ChangeApplicationOwnerResponse>.Failure(currentUserResult.Error!)
                };
            }

            var actor = currentUserResult.Value!;
            var tenant = tenantContextAccessor.CurrentTenant;
            if (tenant is null)
                return Result<ChangeApplicationOwnerResponse>.Failure("Tenant could not be resolved for the current request.");

            var applicationId = new ApplicationId(request.ApplicationId);
            var application = await new GetApplicationByIdQueryObject(applicationId)
                .Apply(applicationRepo.Query())
                .FirstOrDefaultAsync(cancellationToken);

            if (application?.Id is null
                || application.Status == ApplicationStatus.Deleted
                || application.DeletedOn is not null)
            {
                return Result<ChangeApplicationOwnerResponse>.NotFound("Application not found");
            }

            var templateId = application.TemplateVersion?.TemplateId;
            if (templateId is null
                || !await tenantTemplateResolver.IsTemplateInCurrentTenantAsync(templateId, cancellationToken))
            {
                return Result<ChangeApplicationOwnerResponse>.NotFound("Application not found");
            }

            var newOwner = await new GetUserWithAllPermissionsByEmailQueryObject(request.Email)
                .Apply(userRepo.Query())
                .FirstOrDefaultAsync(cancellationToken);

            if (newOwner?.Id is null)
                return Result<ChangeApplicationOwnerResponse>.NotFound("User not found");

            var membership = await tenantMembershipService.GetActiveMembershipAsync(
                tenant.Id,
                newOwner.Id,
                cancellationToken);

            if (membership is null)
                return Result<ChangeApplicationOwnerResponse>.NotFound("User not found");

            if (application.CreatedBy == newOwner.Id)
            {
                return Result<ChangeApplicationOwnerResponse>.Validation(
                    "This user is already the lead applicant");
            }

            var previousOwner = await new GetUserWithAllPermissionsByUserIdQueryObject(application.CreatedBy)
                .Apply(userRepo.Query())
                .FirstOrDefaultAsync(cancellationToken);

            var templateReadGranted = GrantOwnerAccess(
                newOwner,
                application.Id,
                templateId,
                actor.Id!);

            if (previousOwner?.Id is not null && previousOwner.Id != newOwner.Id)
                RemoveApplicationPermissions(previousOwner, application.Id);

            var now = DateTime.UtcNow;
            application.ChangeOwner(newOwner.Id, now, actor.Id!);

            var latestResponseId = await applicationRepo.Query()
                .Where(a => a.Id == application.Id)
                .SelectMany(a => a.Responses)
                .OrderByDescending(r => r.CreatedAtRevision)
                .ThenByDescending(r => r.CreatedOn)
                .Select(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (latestResponseId is not null)
            {
                await projectionEventPublisher.PublishAsync(
                    new ProjectionRequest(
                        application.Id,
                        ProjectionTransition.Saved,
                        application.SourceRevision,
                        latestResponseId,
                        application.SubmittedRevision,
                        templateId,
                        application.TemplateVersionId,
                        now),
                    cancellationToken);
            }

            await unitOfWork.CommitAsync(cancellationToken);

            await userCacheInvalidator.InvalidateApplicationListingsAsync(cancellationToken);
            await userCacheInvalidator.InvalidateForUserAsync(
                newOwner.Email,
                newOwner.ExternalProviderId,
                newOwner.Id,
                cancellationToken);

            if (previousOwner?.Id is not null)
            {
                await userCacheInvalidator.InvalidateForUserAsync(
                    previousOwner.Email,
                    previousOwner.ExternalProviderId,
                    previousOwner.Id,
                    cancellationToken);
            }

            await accessAuditWriter.AppendAsync(
                tenant.Id,
                newOwner.Id,
                newOwner.Email,
                "ApplicationOwnerChanged",
                membership.Role?.Name,
                actor.Id,
                actor.Email,
                $"Lead applicant of {application.ApplicationReference} changed from {previousOwner?.Email ?? application.CreatedBy.Value.ToString()} to {newOwner.Email}.",
                cancellationToken);

            return Result<ChangeApplicationOwnerResponse>.Success(
                new ChangeApplicationOwnerResponse
                {
                    ApplicationId = application.Id.Value,
                    TemplateReadGranted = templateReadGranted
                });
        }
        catch (Exception e)
        {
            return Result<ChangeApplicationOwnerResponse>.Failure(e.Message);
        }
    }

    private bool GrantOwnerAccess(
        User user,
        ApplicationId applicationId,
        TemplateId templateId,
        UserId grantedBy)
    {
        var now = DateTime.UtcNow;
        var resourceKey = applicationId.Value.ToString();

        userFactory.AddPermissionToUser(
            user,
            resourceKey,
            ResourceType.Application,
            [AccessType.Read, AccessType.Write],
            grantedBy,
            applicationId,
            now);

        userFactory.AddPermissionToUser(
            user,
            resourceKey,
            ResourceType.ApplicationFiles,
            [AccessType.Read, AccessType.Write, AccessType.Delete],
            grantedBy,
            applicationId,
            now);

        if (HasTemplateAccess(user, templateId))
            return false;

        userFactory.AddTemplatePermissionToUser(
            user,
            templateId.Value.ToString(),
            [AccessType.Read],
            grantedBy,
            now);

        return true;
    }

    private void RemoveApplicationPermissions(User user, ApplicationId applicationId)
    {
        var scoped = user.Permissions
            .Where(p => p.ApplicationId == applicationId)
            .ToList();

        foreach (var permission in scoped)
            userFactory.RemovePermissionFromUser(user, permission);
    }

    private static bool HasTemplateAccess(User user, TemplateId templateId)
    {
        var key = templateId.Value.ToString();
        return user.Permissions.Any(p =>
            p.ResourceType == ResourceType.Template
            && p.AccessType is AccessType.Read or AccessType.Write or AccessType.Manage
            && (string.Equals(p.ResourceKey, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.ResourceKey, PermissionConstants.AnyResourceKey, StringComparison.OrdinalIgnoreCase)));
    }
}

internal class ChangeApplicationOwnerCommandValidator : AbstractValidator<ChangeApplicationOwnerCommand>
{
    public ChangeApplicationOwnerCommandValidator()
    {
        RuleFor(x => x.ApplicationId)
            .NotEmpty()
            .WithMessage("Application ID is required");

        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("Email is required")
            .EmailAddress()
            .WithMessage("Email must be a valid email address")
            .MaximumLength(256)
            .WithMessage("Email cannot exceed 256 characters");
    }
}
