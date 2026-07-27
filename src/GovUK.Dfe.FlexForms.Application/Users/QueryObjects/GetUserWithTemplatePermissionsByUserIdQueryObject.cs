using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Users.QueryObjects;

/// <summary>
/// Loads a user by id including template permissions (for tenant removal / access changes).
/// </summary>
public sealed class GetUserWithTemplatePermissionsByUserIdQueryObject(UserId userId)
    : IQueryObject<User>
{
    public IQueryable<User> Apply(IQueryable<User> query) =>
        query
            .Include(u => u.TemplatePermissions)
            .Where(u => u.Id == userId);
}
