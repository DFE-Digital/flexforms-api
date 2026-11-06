using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Applications.QueryObjects;

/// <summary>
/// Application-level permissions granted by a specific user (the inviter) to someone else, limited
/// to the given applications. Pass the application ids as a query, not a materialised list: users
/// with thousands of applications turned a list into a huge SQL parameter list.
/// </summary>
public sealed class GetApplicationInviteesGrantedByUserQueryObject(
    UserId grantedBy,
    IQueryable<ApplicationId> applicationIds)
    : IQueryObject<Permission>
{
    public IQueryable<Permission> Apply(IQueryable<Permission> query) =>
        query
            .AsNoTracking()
            .Include(p => p.User)
            .Where(p =>
                p.GrantedBy == grantedBy
                && p.ResourceType == ResourceType.Application
                && p.ApplicationId != null
                && applicationIds.Contains(p.ApplicationId)
                && p.UserId != grantedBy);
}
