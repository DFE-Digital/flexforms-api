using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Identifiers;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Prism.Queries;

/// <summary>
/// Current source state of one application for the Prism projector, including deleted applications.
/// </summary>
public sealed record GetPrismApplicationStateQuery(Guid ApplicationId)
    : IRequest<Result<PrismApplicationStateDto>>;

public sealed class GetPrismApplicationStateQueryHandler(
    IEaRepository<Domain.Entities.Application> applicationRepo,
    ITenantTemplateResolver tenantTemplateResolver)
    : IRequestHandler<GetPrismApplicationStateQuery, Result<PrismApplicationStateDto>>
{
    public async Task<Result<PrismApplicationStateDto>> Handle(
        GetPrismApplicationStateQuery request,
        CancellationToken cancellationToken)
    {
        var applicationId = new ApplicationId(request.ApplicationId);

        var application = await applicationRepo.Query()
            .AsNoTracking()
            .Where(a => a.Id == applicationId)
            .Select(a => new
            {
                a.ApplicationReference,
                a.SourceRevision,
                a.SubmittedRevision,
                a.Status,
                a.DeletedOn,
                a.TemplateVersionId,
                a.TemplateVersion!.TemplateId,
                a.CreatedOn,
                a.LastModifiedOn
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (application is null
            || !await tenantTemplateResolver.IsTemplateInCurrentTenantAsync(application.TemplateId, cancellationToken))
        {
            return Result<PrismApplicationStateDto>.NotFound("Application not found");
        }

        // Responses are immutable; bounding by the revision read above gives a consistent view
        // even if a save commits between the two queries.
        var responses = applicationRepo.Query()
            .AsNoTracking()
            .Where(a => a.Id == applicationId)
            .SelectMany(a => a.Responses)
            .Where(r => r.CreatedAtRevision <= application.SourceRevision);

        var latest = await responses
            .OrderByDescending(r => r.CreatedAtRevision)
            .ThenByDescending(r => r.CreatedOn)
            .Select(r => new { r.Id, r.CreatedAtRevision, r.ResponseBody })
            .FirstOrDefaultAsync(cancellationToken);

        ResponseId? submittedResponseId = null;
        Guid? submissionId = null;
        if (application.SubmittedRevision is { } submittedRevision)
        {
            submittedResponseId = await responses
                .Where(r => r.CreatedAtRevision <= submittedRevision)
                .OrderByDescending(r => r.CreatedAtRevision)
                .ThenByDescending(r => r.CreatedOn)
                .Select(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken);

            submissionId = ApplicationProjectionIdentifiers.SubmissionId(request.ApplicationId, submittedRevision);
        }

        return Result<PrismApplicationStateDto>.Success(new PrismApplicationStateDto(
            request.ApplicationId,
            application.ApplicationReference,
            application.SourceRevision,
            application.Status,
            application.Status == ApplicationStatus.Deleted || application.DeletedOn is not null,
            application.DeletedOn,
            application.TemplateId.Value,
            application.TemplateVersionId.Value,
            application.CreatedOn,
            application.LastModifiedOn,
            latest?.Id?.Value,
            latest?.CreatedAtRevision,
            latest?.ResponseBody,
            application.SubmittedRevision,
            submissionId,
            submittedResponseId?.Value));
    }
}
