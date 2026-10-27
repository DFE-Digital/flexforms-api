using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Applications.QueryObjects;

/// <summary>
/// Users with an application permission for the application, other than its creator.
/// Each user's permissions and role are loaded only when <paramref name="includePermissionDetails"/> is set:
/// the creator alone can hold thousands of permission rows across their applications.
/// </summary>
public sealed class GetContributorsForApplicationQueryObject(
    ApplicationId applicationId,
    UserId creatorId,
    bool includePermissionDetails = false)
    : IQueryObject<User>
{
    public IQueryable<User> Apply(IQueryable<User> query)
    {
        var contributors = query
            .Where(u => u.Id != creatorId)
            .Where(u => u.Permissions.Any(p => p.ApplicationId == applicationId && p.ResourceType == ResourceType.Application));

        return includePermissionDetails
            ? contributors.Include(u => u.Permissions).Include(u => u.Role)
            : contributors;
    }
}
