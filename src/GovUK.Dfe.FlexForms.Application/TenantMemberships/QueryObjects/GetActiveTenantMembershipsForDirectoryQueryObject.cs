using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.TenantMemberships.QueryObjects;

/// <summary>
/// Active tenant memberships with user and role, without permission graphs.
/// Use this for paged directory listing.
/// </summary>
public sealed class GetActiveTenantMembershipsForDirectoryQueryObject(
    Guid tenantId,
    UserId? userId = null,
    string? email = null,
    string? searchTerm = null,
    string? role = null)
    : IQueryObject<TenantMembership>
{
    public IQueryable<TenantMembership> Apply(IQueryable<TenantMembership> query)
    {
        query = query
            .AsNoTracking()
            .Include(m => m.Role)
            .Include(m => m.User)
            .Where(m => m.TenantId == tenantId && m.IsActive);

        if (userId is not null)
            query = query.Where(m => m.UserId == userId);

        if (!string.IsNullOrWhiteSpace(email))
        {
            var normalized = email.Trim().ToLower();
            query = query.Where(m => m.User != null && m.User.Email.ToLower() == normalized);
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
            query = new GetTenantMembershipsBySearchTermQueryObject(searchTerm).Apply(query);

        if (!string.IsNullOrWhiteSpace(role))
            query = new GetTenantMembershipsByRoleNameQueryObject(role).Apply(query);

        return query
            .OrderBy(m => m.User!.Name)
            .ThenBy(m => m.User!.Email);
    }
}
