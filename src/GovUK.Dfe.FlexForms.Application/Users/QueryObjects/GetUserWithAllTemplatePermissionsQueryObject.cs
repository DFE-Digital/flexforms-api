using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Users.QueryObjects
{
    /// <summary>
    /// Filters to one user by normalized email, and includes permissions (including Template grants).
    /// </summary>
    public sealed class GetUserWithAllTemplatePermissionsQueryObject(string email)
        : IQueryObject<User>
    {
        private readonly string _normalizedEmail = email.Trim().ToLowerInvariant();

        public IQueryable<User> Apply(IQueryable<User> query)
        {
            return query
                .Where(u => u.Email.ToLower() == _normalizedEmail)
                .Include(u => u.Permissions);
        }
    }
}
