using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Application.Templates.QueryObjects;

/// <summary>
/// Live templates whose ids are in the supplied set.
/// </summary>
public sealed class GetLiveTemplatesByIdsQueryObject(IEnumerable<TemplateId> templateIds)
    : IQueryObject<Template>
{
    private readonly HashSet<Guid> _templateIds = templateIds
        .Select(id => id.Value)
        .ToHashSet();

    public IQueryable<Template> Apply(IQueryable<Template> query)
    {
        if (_templateIds.Count == 0)
            return query.Where(_ => false);

        return query.Where(t => t.IsLive && t.Id != null && _templateIds.Contains(t.Id.Value));
    }
}
