using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Templates.QueryObjects;

/// <summary>
/// Finds a template in a tenant by name (case-insensitive).
/// </summary>
public sealed class GetTemplateByTenantAndNameQueryObject(Guid tenantId, string name)
    : IQueryObject<Template>
{
    public IQueryable<Template> Apply(IQueryable<Template> query) =>
        query.Where(t =>
            t.TenantId == tenantId &&
            t.Name.ToLower() == name.Trim().ToLower());
}
