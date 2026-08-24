using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Users.QueryObjects;

/// <summary>
/// Filters to one user by normalized email and includes role and permissions.
/// </summary>
public sealed class GetUserWithAllPermissionsByEmailQueryObject(string email) : IQueryObject<User>
{
    private readonly string _normalizedEmail = email.Trim().ToLowerInvariant();

    /// <inheritdoc />
    public IQueryable<User> Apply(IQueryable<User> query) =>
        query
            .Where(u => u.Email.ToLower() == _normalizedEmail)
            .Include(u => u.Permissions)
            .Include(u => u.Role)
            .AsSplitQuery();
}
