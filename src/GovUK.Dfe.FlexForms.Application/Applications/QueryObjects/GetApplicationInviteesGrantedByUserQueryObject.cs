using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Applications.QueryObjects;

/// <summary>
/// Application-level permissions granted by a specific user (the inviter).
/// </summary>
public sealed class GetApplicationInviteesGrantedByUserQueryObject(
    UserId grantedBy,
    IReadOnlyCollection<ApplicationId> applicationIds)
    : IQueryObject<Permission>
{
    private readonly HashSet<ApplicationId> _applicationIds = applicationIds.ToHashSet();

    public IQueryable<Permission> Apply(IQueryable<Permission> query) =>
        query
            .AsNoTracking()
            .Include(p => p.User)
            .Where(p =>
                p.GrantedBy == grantedBy
                && p.ResourceType == ResourceType.Application
                && p.ApplicationId != null
                && _applicationIds.Contains(p.ApplicationId)
                && p.UserId != grantedBy);
}
