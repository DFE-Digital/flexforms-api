using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Application.Users.Queries
{
    public sealed record GetMyPermissionsQuery()
        : IRequest<Result<UserAuthorizationDto>>;

    public sealed class GetMyPermissionsQueryHandler(
        IHttpContextAccessor httpContextAccessor,
        IEaRepository<User> userRepo,
        ISender mediator)
        : IRequestHandler<GetMyPermissionsQuery, Result<UserAuthorizationDto>>
    {
        public async Task<Result<UserAuthorizationDto>> Handle(
            GetMyPermissionsQuery request,
            CancellationToken cancellationToken)
        {
            var user = httpContextAccessor.HttpContext?.User;
            if (user is null || !user.Identity?.IsAuthenticated == true)
                return Result<UserAuthorizationDto>.Forbid("Not authenticated");

            var principalId = EntraClientIdentity.PickEmail(user)
                              ?? EntraClientIdentity.PickClientId(user);

            if (string.IsNullOrEmpty(principalId))
                return Result<UserAuthorizationDto>.Forbid("No user identifier");

            User? dbUser;
            if (principalId.Contains('@'))
            {
                dbUser = await (new GetUserByEmailQueryObject(principalId))
                    .Apply(userRepo.Query().AsNoTracking())
                    .FirstOrDefaultAsync(cancellationToken);
            }
            else
            {
                dbUser = await (new GetUserByExternalProviderIdQueryObject(principalId))
                    .Apply(userRepo.Query().AsNoTracking())
                .FirstOrDefaultAsync(cancellationToken);
            }

            if (dbUser is null)
                return Result<UserAuthorizationDto>.NotFound("User not found");

            // The caller is authenticated and matched to this user by token identity (email or external id).
            // Returning their own permissions is safe without a separate User:Read claim, which invited
            // contributors may not have been provisioned with historically.
            return await mediator.Send(new GetAllUserPermissionsQuery(dbUser.Id), cancellationToken);

        }
    }
}
