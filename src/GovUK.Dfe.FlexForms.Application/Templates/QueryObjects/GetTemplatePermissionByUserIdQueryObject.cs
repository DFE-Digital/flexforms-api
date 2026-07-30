using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Templates.QueryObjects;

/// <summary>
/// Finds Template permission grants for a user + template from the unified Permissions store.
/// </summary>
public sealed class GetTemplatePermissionByUserIdQueryObject(UserId userId, Guid templateId)
    : IQueryObject<Permission>
{
    public IQueryable<Permission> Apply(IQueryable<Permission> query)
    {
        var key = templateId.ToString();
        return query
            .Include(x => x.User)
            .Where(x =>
                x.User != null
                && x.User.Id == userId
                && x.ResourceType == ResourceType.Template
                && x.ResourceKey == key);
    }
}
