using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Applications.QueryObjects;

public sealed class GetApplicationsCreatedByUserQueryObject(UserId createdBy)
    : IQueryObject<Domain.Entities.Application>
{
    public IQueryable<Domain.Entities.Application> Apply(IQueryable<Domain.Entities.Application> query) =>
        query
            .AsNoTracking()
            .Include(a => a.TemplateVersion)
            .ThenInclude(tv => tv!.Template)
            .Where(a => a.CreatedBy == createdBy);
}
