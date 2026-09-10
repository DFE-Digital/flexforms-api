using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;

namespace GovUK.Dfe.FlexForms.Application.TenantMemberships.QueryObjects;

/// <summary>
/// Free-text filter matching part of the member's name or email address.
/// </summary>
public sealed class GetTenantMembershipsBySearchTermQueryObject(string searchTerm)
    : IQueryObject<TenantMembership>
{
    private readonly string _searchTerm = searchTerm.Trim().ToLowerInvariant();

    public IQueryable<TenantMembership> Apply(IQueryable<TenantMembership> query) =>
        query.Where(m =>
            m.User != null
            && (m.User.Name.ToLower().Contains(_searchTerm)
                || m.User.Email.ToLower().Contains(_searchTerm)));
}
