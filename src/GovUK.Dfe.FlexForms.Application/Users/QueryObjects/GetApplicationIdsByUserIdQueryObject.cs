using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Users.QueryObjects;

/// <summary>
/// Projects distinct application IDs from <c>ea.Permissions</c> for a user —
/// without hydrating the full permission graph.
/// </summary>
public sealed class GetApplicationIdsByUserIdQueryObject(UserId userId)
{
    public IQueryable<ApplicationId> Apply(IQueryable<User> query) =>
        query
            .Where(u => u.Id == userId)
            .SelectMany(u => u.Permissions)
            .Where(p => p.ApplicationId != null && p.ResourceType == ResourceType.Application)
            .Select(p => p.ApplicationId!)
            .Distinct();
}

/// <summary>
/// Projects distinct application IDs from <c>ea.Permissions</c> for an external provider id.
/// </summary>
public sealed class GetApplicationIdsByExternalProviderIdQueryObject(string externalProviderId)
{
    public IQueryable<ApplicationId> Apply(IQueryable<User> query) =>
        query
            .Where(u => u.ExternalProviderId == externalProviderId)
            .SelectMany(u => u.Permissions)
            .Where(p => p.ApplicationId != null && p.ResourceType == ResourceType.Application)
            .Select(p => p.ApplicationId!)
            .Distinct();
}
