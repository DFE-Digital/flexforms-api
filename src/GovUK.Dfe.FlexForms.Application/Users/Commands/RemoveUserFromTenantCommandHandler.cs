using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Users.Commands;

/// <summary>
/// Removes a user from the current tenant by clearing their permissions on tenant templates.
/// The user account and role are left intact.
/// </summary>
public sealed record RemoveUserFromTenantCommand(Guid UserId)
    : IRequest<Result<bool>>;

/// <summary>
/// Handles <see cref="RemoveUserFromTenantCommand"/>.
/// </summary>
public sealed class RemoveUserFromTenantCommandHandler(
    IEaRepository<User> userRepository,
    IUnitOfWork unitOfWork,
    IUserFactory userFactory,
    ITenantTemplateCatalogue tenantTemplateCatalogue,
    IPermissionCheckerService permissionCheckerService,
    IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<RemoveUserFromTenantCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        RemoveUserFromTenantCommand command,
        CancellationToken cancellationToken)
    {
        if (!permissionCheckerService.IsAdmin())
            return Result<bool>.Forbid("Only administrators can remove users from the tenant");

        var actingEmail = httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Email);
        var userId = new UserId(command.UserId);

        var user = await userRepository.Query()
            .Include(u => u.TemplatePermissions)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
            return Result<bool>.NotFound("User not found");

        if (!string.IsNullOrWhiteSpace(actingEmail)
            && string.Equals(user.Email, actingEmail, StringComparison.OrdinalIgnoreCase))
        {
            return Result<bool>.Failure("You cannot remove yourself from the tenant");
        }

        var catalogueIds = await tenantTemplateCatalogue.GetTemplateIdsAsync(cancellationToken);
        if (catalogueIds.Count == 0)
            return Result<bool>.Success(true);

        var catalogueSet = catalogueIds.ToHashSet();
        var tenantTemplateIds = user.TemplatePermissions
            .Where(tp => catalogueSet.Contains(tp.TemplateId))
            .Select(tp => tp.TemplateId)
            .Distinct()
            .ToList();

        if (tenantTemplateIds.Count > 0)
            userFactory.RemoveTemplatePermissionsFromUser(user, tenantTemplateIds);

        await unitOfWork.CommitAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}
