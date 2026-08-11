using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Applications.QueryObjects;

/// <summary>
/// Optimized read/query for "application by reference" that:
/// - Projects directly to ApplicationDto to avoid loading entire entity graphs
/// - Only fetches JsonSchema when includeSchema=true
/// - Latest response is loaded separately via <see cref="GetLatestApplicationResponseByApplicationIdQueryObject"/>
///   to avoid EF generating a full-table ROW_NUMBER() scan over ApplicationResponses.
/// </summary>
public sealed class GetApplicationByReferenceDtoQueryObject(string applicationReference, bool includeSchema = true)
{
    public IQueryable<ApplicationDto> Apply(IQueryable<Domain.Entities.Application> query)
    {
        // Note: we intentionally do NOT use Include(...) here.
        // Projection ensures EF selects only the referenced columns.
        return query
            .AsNoTracking()
            .Where(a => a.ApplicationReference == applicationReference)
            .Select(a => new ApplicationDto
            {
                ApplicationId = a.Id!.Value,
                ApplicationReference = a.ApplicationReference,
                TemplateVersionId = a.TemplateVersionId.Value,
                TemplateName = a.TemplateVersion!.Template!.Name,
                Status = a.Status,
                DateCreated = a.CreatedOn,
                DateSubmitted = a.Status == GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums.ApplicationStatus.Submitted
                    ? a.LastModifiedOn
                    : null,
                TemplateSchema = new TemplateSchemaDto
                {
                    TemplateId = a.TemplateVersion!.Template!.Id!.Value,
                    TemplateVersionId = a.TemplateVersion!.Id!.Value,
                    VersionNumber = includeSchema ? a.TemplateVersion!.VersionNumber : string.Empty,
                    JsonSchema = includeSchema ? a.TemplateVersion!.JsonSchema : string.Empty
                },
                CreatedBy = a.CreatedByUser == null
                    ? null
                    : new UserDto
                    {
                        UserId = a.CreatedByUser.Id!.Value,
                        Name = a.CreatedByUser.Name,
                        Email = a.CreatedByUser.Email
                    }
            });
    }
}
