using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Templates.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace GovUK.Dfe.FlexForms.Application.Templates.Queries
{
    public sealed record GetCustomApplicationStatusesQuery(Guid TemplateId)
       : IRequest<Result<IReadOnlyCollection<CustomApplicationStatusDto>>>;

    public sealed class GetCustomApplicationStatusesQueryHandler(
        IEaRepository<CustomApplicationStatus> repo,
        ITenantTemplateResolver tenantTemplateResolver)
        : IRequestHandler<GetCustomApplicationStatusesQuery, Result<IReadOnlyCollection<CustomApplicationStatusDto>>>
    {
        public async Task<Result<IReadOnlyCollection<CustomApplicationStatusDto>>> Handle(
            GetCustomApplicationStatusesQuery request,
            CancellationToken cancellationToken)
        {
            try
            {
                var templateId = new TemplateId(request.TemplateId);
                if (!await tenantTemplateResolver.IsTemplateInCurrentTenantAsync(templateId, cancellationToken))
                {
                    return Result<IReadOnlyCollection<CustomApplicationStatusDto>>.NotFound("Template not found");
                }

                // Load existing custom statuses for the template
                var existing = await new GetCustomApplicationStatusesByTemplateIdQueryObject(request.TemplateId)
                    .Apply(repo.Query().AsNoTracking())
                    .ToListAsync(cancellationToken);

                // For every ApplicationStatus enum value, return either the custom status (if exists) or a blank-label entry
                var statuses = Enum.GetValues(typeof(ApplicationStatus))
                    .Cast<ApplicationStatus>();

                var resultList = statuses
                    .Select(s =>
                    {
                        var match = existing.FirstOrDefault(e => e.ApplicationStatus == s);
                        return new CustomApplicationStatusDto
                        {
                            CustomApplicationStatusId = match?.Id?.Value,
                            TemplateId = request.TemplateId,
                            ApplicationStatus = s,
                            Label = match?.Label,
                            CreatedOn = match?.CreatedOn ?? default,
                            CreatedBy = match?.CreatedBy?.Value ?? Guid.Empty
                        };
                    })
                    .ToList();

                return Result<IReadOnlyCollection<CustomApplicationStatusDto>>.Success(resultList.AsReadOnly());
            }
            catch (Exception e)
            {
                return Result<IReadOnlyCollection<CustomApplicationStatusDto>>.Failure(e.ToString());
            }
        }
    }
}
