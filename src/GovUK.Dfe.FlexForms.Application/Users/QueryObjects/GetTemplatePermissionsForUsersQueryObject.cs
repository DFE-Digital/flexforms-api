using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Users.QueryObjects;

/// <summary>
/// Template grants for a page of users, without application-level permissions.
/// </summary>
public sealed class GetTemplatePermissionsForUsersQueryObject(IReadOnlyCollection<UserId> userIds)
    : IQueryObject<Permission>
{
    private readonly HashSet<UserId> _userIds = userIds.ToHashSet();

    public IQueryable<Permission> Apply(IQueryable<Permission> query)
    {
        if (_userIds.Count == 0)
            return query.Where(_ => false);

        return query
            .AsNoTracking()
            .Where(p =>
                p.ResourceType == ResourceType.Template
                && _userIds.Contains(p.UserId));
    }
}
