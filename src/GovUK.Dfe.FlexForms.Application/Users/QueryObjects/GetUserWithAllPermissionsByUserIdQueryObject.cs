using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Users.QueryObjects
{
    /// <summary>
    /// Filters to one user by UserId, and includes all Permission children.
    /// </summary>
    public sealed class GetUserWithAllPermissionsByUserIdQueryObject(UserId userId)
        : IQueryObject<User>
    {
        public IQueryable<User> Apply(IQueryable<User> query)
        {
            return query
                .Where(u => u.Id == userId)
                .Include(u => u.Permissions)
                .Include(u => u.Role);
        }
    }
}
