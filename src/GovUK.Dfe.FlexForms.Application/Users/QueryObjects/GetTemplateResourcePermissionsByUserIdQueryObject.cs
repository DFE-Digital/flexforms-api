using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Application.Users.QueryObjects;

/// <summary>
/// Loads only Template resource permission rows for listing filters —
/// without hydrating Application permissions or Role.
/// </summary>
public sealed class GetTemplateResourcePermissionsByUserIdQueryObject(UserId userId)
{
    public IQueryable<Permission> Apply(IQueryable<User> query) =>
        query
            .Where(u => u.Id == userId)
            .SelectMany(u => u.Permissions)
            .Where(p => p.ResourceType == ResourceType.Template);
}

/// <summary>
/// Loads only Template resource permission rows by external provider id.
/// </summary>
public sealed class GetTemplateResourcePermissionsByExternalProviderIdQueryObject(string externalProviderId)
{
    public IQueryable<Permission> Apply(IQueryable<User> query) =>
        query
            .Where(u => u.ExternalProviderId == externalProviderId)
            .SelectMany(u => u.Permissions)
            .Where(p => p.ResourceType == ResourceType.Template);
}
