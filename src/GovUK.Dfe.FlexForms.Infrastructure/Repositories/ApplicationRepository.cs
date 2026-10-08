using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using Microsoft.EntityFrameworkCore;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Infrastructure.Repositories;

public sealed class ApplicationRepository(ExternalApplicationsContext dbContext)
    : EaRepository<Application>(dbContext), IApplicationRepository
{
    public async Task<ApplicationResponse?> GetLatestResponseAsync(
        ApplicationId applicationId,
        CancellationToken cancellationToken) =>
        await DbContext.ApplicationResponses
            .AsNoTracking()
            .Where(r => r.ApplicationId == applicationId)
            .OrderByDescending(r => r.CreatedOn)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<ApplicationId, ApplicationResponse>> GetLatestResponsesAsync(
        IReadOnlyCollection<ApplicationId> applicationIds,
        CancellationToken cancellationToken)
    {
        if (applicationIds is null || applicationIds.Count == 0)
            return new Dictionary<ApplicationId, ApplicationResponse>();

        var ids = applicationIds.Distinct().ToList();

        // Pull candidate rows for the page, then keep the newest per application in memory.
        // Page sizes are small; this avoids N+1 while staying portable across providers.
        var responses = await DbContext.ApplicationResponses
            .AsNoTracking()
            .Where(r => ids.Contains(r.ApplicationId))
            .OrderByDescending(r => r.CreatedOn)
            .ToListAsync(cancellationToken);

        return responses
            .GroupBy(r => r.ApplicationId)
            .ToDictionary(g => g.Key, g => g.First());
    }

    public async Task<(string ApplicationReference, ApplicationResponse Response)?> AppendResponseVersionAsync(
        ApplicationId applicationId,
        ApplicationResponse response,
        DateTime lastModifiedOn,
        UserId lastModifiedBy,
        CancellationToken cancellationToken,
        Func<ResponseVersionAppended, CancellationToken, Task>? beforeCommit = null)
    {
        // Minimal read: only fetch what the return payload and projection event need, plus an existence check.
        var application = await DbContext.Applications
            .AsNoTracking()
            .Where(a => a.Id == applicationId)
            .Select(a => new
            {
                a.ApplicationReference,
                a.TemplateVersionId,
                TemplateId = a.TemplateVersion != null ? a.TemplateVersion.TemplateId : null
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (application is null)
            return null;

        await using var tx = await DbContext.Database.BeginTransactionAsync(cancellationToken);

        // Update last-modified tracking and bump the revision without loading the Application aggregate graph.
        // The row stays write-locked until commit, so the revision read back below is ours alone.
        var updated = await DbContext.Applications
            .Where(a => a.Id == applicationId)
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(a => a.Status, a => a.Status == ApplicationStatus.Submitted ? a.Status : ApplicationStatus.InProgress)
                    .SetProperty(a => a.LastModifiedOn, lastModifiedOn)
                    .SetProperty(a => a.LastModifiedBy, lastModifiedBy)
                    .SetProperty(a => a.SourceRevision, a => a.SourceRevision + 1),
                cancellationToken);

        if (updated == 0)
            return null;

        var sourceRevision = await DbContext.Applications
            .AsNoTracking()
            .Where(a => a.Id == applicationId)
            .Select(a => a.SourceRevision)
            .SingleAsync(cancellationToken);

        response.AssignCreatedAtRevision(sourceRevision);
        DbContext.ApplicationResponses.Add(response);

        if (beforeCommit is not null)
        {
            await beforeCommit(
                new ResponseVersionAppended(
                    applicationId,
                    response.Id!,
                    sourceRevision,
                    application.TemplateVersionId,
                    application.TemplateId),
                cancellationToken);
        }

        await DbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return (application.ApplicationReference, response);
    }
}


