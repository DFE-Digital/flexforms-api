using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Templates.QueryObjects;

/// <summary>
/// Finds Template permission grants for an external provider user + template from Permissions.
/// </summary>
public sealed class GetTemplatePermissionByExternalProviderIdQueryObject(string externalProviderId, Guid templateId)
    : IQueryObject<Permission>
{
    public IQueryable<Permission> Apply(IQueryable<Permission> query)
    {
        var key = templateId.ToString();
        return query
            .Include(x => x.User)
            .Where(x =>
                x.User != null
                && x.User.ExternalProviderId == externalProviderId
                && x.ResourceType == ResourceType.Template
                && x.ResourceKey == key);
    }
}
