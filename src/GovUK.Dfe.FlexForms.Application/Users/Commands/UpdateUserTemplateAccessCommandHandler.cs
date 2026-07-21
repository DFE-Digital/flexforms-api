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
/// Replaces a user's form (template) access within the current tenant.
/// Permissions for templates outside the tenant catalogue are left unchanged.
/// </summary>
public sealed record UpdateUserTemplateAccessCommand(
    Guid UserId,
    IReadOnlyCollection<Guid> TemplateIds)
    : IRequest<Result<TenantUserDto>>;

/// <summary>
/// Handles <see cref="UpdateUserTemplateAccessCommand"/>.
/// </summary>
public sealed class UpdateUserTemplateAccessCommandHandler(
    IEaRepository<User> userRepository,
    IEaRepository<Template> templateRepository,
    IUnitOfWork unitOfWork,
    IUserFactory userFactory,
    ITenantTemplateCatalogue tenantTemplateCatalogue,
    IPermissionCheckerService permissionCheckerService,
    IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<UpdateUserTemplateAccessCommand, Result<TenantUserDto>>
{
    public async Task<Result<TenantUserDto>> Handle(
        UpdateUserTemplateAccessCommand command,
        CancellationToken cancellationToken)
    {
        if (!permissionCheckerService.IsAdmin())
            return Result<TenantUserDto>.Forbid("Only administrators can update user template access");

        var catalogueIds = await tenantTemplateCatalogue.GetTemplateIdsAsync(cancellationToken);
        var catalogueSet = catalogueIds.ToHashSet();

        var desiredIds = (command.TemplateIds ?? Array.Empty<Guid>())
            .Distinct()
            .Select(id => new TemplateId(id))
            .ToList();

        foreach (var templateId in desiredIds)
        {
            if (!catalogueSet.Contains(templateId))
                return Result<TenantUserDto>.Failure($"Template {templateId.Value} does not belong to the current tenant");
        }

        var userId = new UserId(command.UserId);
        var user = await userRepository.Query()
            .Include(u => u.Role)
            .Include(u => u.TemplatePermissions)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
            return Result<TenantUserDto>.NotFound("User not found");

        var grantedById = await ResolveGrantedByUserIdAsync(cancellationToken);
        if (grantedById is null)
            return Result<TenantUserDto>.Failure("Could not resolve the acting administrator");

        var desiredSet = desiredIds.ToHashSet();
        var currentTenantTemplateIds = user.TemplatePermissions
            .Where(tp => catalogueSet.Contains(tp.TemplateId))
            .Select(tp => tp.TemplateId)
            .Distinct()
            .ToList();

        var toRemove = currentTenantTemplateIds.Where(id => !desiredSet.Contains(id)).ToList();
        if (toRemove.Count > 0)
            userFactory.RemoveTemplatePermissionsFromUser(user, toRemove);

        var now = DateTime.UtcNow;
        foreach (var templateId in desiredIds)
        {
            userFactory.EnsureUserHasTemplatePermission(user, templateId, grantedById, now);
        }

        await unitOfWork.CommitAsync(cancellationToken);

        var templates = await templateRepository.Query()
            .AsNoTracking()
            .Where(t => t.Id != null && desiredSet.Contains(t.Id))
            .ToListAsync(cancellationToken);

        var templateLookup = templates.ToDictionary(t => t.Id!.Value, t => t);
        var roleName = user.Role?.Name
            ?? Domain.Common.RoleNames.FromRoleId(user.RoleId.Value)
            ?? string.Empty;

        return Result<TenantUserDto>.Success(new TenantUserDto
        {
            UserId = user.Id!.Value,
            Name = user.Name,
            Email = user.Email,
            Role = roleName,
            Templates = desiredIds
                .Select(id =>
                {
                    templateLookup.TryGetValue(id.Value, out var template);
                    return new TenantUserTemplateDto
                    {
                        TemplateId = id.Value,
                        TemplateName = template?.Name ?? id.Value.ToString(),
                        IsLive = template?.IsLive ?? false
                    };
                })
                .OrderBy(t => t.TemplateName)
                .ToList()
        });
    }

    private async Task<UserId?> ResolveGrantedByUserIdAsync(CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var email = principal?.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var adminUser = await new GetUserByEmailQueryObject(email)
            .Apply(userRepository.Query().AsNoTracking())
            .FirstOrDefaultAsync(cancellationToken);

        return adminUser?.Id;
    }
}
