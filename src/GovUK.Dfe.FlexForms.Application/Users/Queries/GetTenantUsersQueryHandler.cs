using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Templates.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Users.Queries;

/// <summary>
/// Lists users who have form access within the current tenant.
/// </summary>
public sealed record GetTenantUsersQuery
    : IRequest<Result<IReadOnlyCollection<TenantUserDto>>>;

/// <summary>
/// Handles <see cref="GetTenantUsersQuery"/>.
/// </summary>
public sealed class GetTenantUsersQueryHandler(
    IEaRepository<User> userRepository,
    IEaRepository<Template> templateRepository,
    ITenantTemplateCatalogue tenantTemplateCatalogue,
    IPermissionCheckerService permissionCheckerService)
    : IRequestHandler<GetTenantUsersQuery, Result<IReadOnlyCollection<TenantUserDto>>>
{
    public async Task<Result<IReadOnlyCollection<TenantUserDto>>> Handle(
        GetTenantUsersQuery request,
        CancellationToken cancellationToken)
    {
        if (!permissionCheckerService.IsAdmin())
            return Result<IReadOnlyCollection<TenantUserDto>>.Forbid("Only administrators can list tenant users");

        var catalogueIds = await tenantTemplateCatalogue.GetTemplateIdsAsync(cancellationToken);
        if (catalogueIds.Count == 0)
            return Result<IReadOnlyCollection<TenantUserDto>>.Success(Array.Empty<TenantUserDto>());

        var catalogueSet = catalogueIds.ToHashSet();

        var users = await userRepository.Query()
            .AsNoTracking()
            .Include(u => u.Role)
            .Include(u => u.TemplatePermissions)
            .Where(u => u.TemplatePermissions.Any(tp => catalogueSet.Contains(tp.TemplateId)))
            .OrderBy(u => u.Name)
            .ToListAsync(cancellationToken);

        var templates = await new GetTemplatesByIdsQueryObject(catalogueIds)
            .Apply(templateRepository.Query().AsNoTracking())
            .ToListAsync(cancellationToken);

        var templateLookup = templates.ToDictionary(
            t => t.Id!.Value,
            t => t);

        var result = users.Select(user =>
        {
            var roleName = user.Role?.Name
                ?? RoleNames.FromRoleId(user.RoleId.Value)
                ?? string.Empty;

            var userTemplates = user.TemplatePermissions
                .Where(tp => catalogueSet.Contains(tp.TemplateId))
                .Select(tp => tp.TemplateId.Value)
                .Distinct()
                .Select(templateId =>
                {
                    templateLookup.TryGetValue(templateId, out var template);
                    return new TenantUserTemplateDto
                    {
                        TemplateId = templateId,
                        TemplateName = template?.Name ?? templateId.ToString(),
                        IsLive = template?.IsLive ?? false
                    };
                })
                .OrderBy(t => t.TemplateName)
                .ToList();

            return new TenantUserDto
            {
                UserId = user.Id!.Value,
                Name = user.Name,
                Email = user.Email,
                Role = roleName,
                Templates = userTemplates
            };
        }).ToList();

        return Result<IReadOnlyCollection<TenantUserDto>>.Success(result);
    }
}
