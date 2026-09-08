using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Loads the authenticated user entity for the current HTTP request.
/// </summary>
public sealed class AuthenticatedUserService(
    IHttpContextAccessor httpContextAccessor,
    IEaRepository<User> userRepo) : IAuthenticatedUserService
{
    /// <inheritdoc />
    public async Task<Result<User>> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext?.User is not ClaimsPrincipal user || user.Identity?.IsAuthenticated != true)
            return Result<User>.Forbid("Not authenticated");

        var principalId = EntraClientIdentity.PickClientId(user)
                          ?? EntraClientIdentity.PickEmail(user);

        if (string.IsNullOrEmpty(principalId))
            return Result<User>.Forbid("No user identifier");

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

        return dbUser is null
            ? Result<User>.NotFound("User not found")
            : Result<User>.Success(dbUser);
    }
}
