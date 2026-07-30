using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Users.QueryObjects
{
    public sealed class GetUserWithAllTemplatePermissionsByExternalIdQueryObject(string externalProviderId)
        : IQueryObject<User>
    {
        public IQueryable<User> Apply(IQueryable<User> query)
        {
            return query
                .Where(u => u.ExternalProviderId == externalProviderId)
                .Include(u => u.Permissions);
        }
    }
}
