using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Application.Templates.QueryObjects;

/// <summary>
/// Loads templates whose IDs are in the supplied set, ordered by name.
/// Versions are not loaded: each one holds the full template JSON, so project only the version fields you need.
/// </summary>
public sealed class GetTemplatesByIdsQueryObject(IEnumerable<TemplateId> templateIds)
    : IQueryObject<Template>
{
    private readonly HashSet<TemplateId> _templateIds = templateIds.ToHashSet();

    public IQueryable<Template> Apply(IQueryable<Template> query)
    {
        if (_templateIds.Count == 0)
        {
            return query.Where(_ => false);
        }

        // Compare TemplateId value objects (not .Value) so EF can translate Contains to SQL IN.
        return query
            .Where(t => t.Id != null && _templateIds.Contains(t.Id))
            .OrderBy(t => t.Name);
    }
}
