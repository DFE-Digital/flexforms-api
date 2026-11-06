using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Applications.QueryObjects;

/// <summary>
/// Application-level permissions granted by a specific user (the inviter) to someone else.
/// Narrow to specific applications in memory, not in SQL: users with thousands of applications
/// turn an application-id filter into a huge parameter list. GrantedBy alone seeks IX_Permissions_GrantedBy.
/// </summary>
public sealed class GetApplicationInviteesGrantedByUserQueryObject(UserId grantedBy)
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
                && p.UserId != grantedBy);
}
