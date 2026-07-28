using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Templates.QueryObjects;
using GovUK.Dfe.FlexForms.Application.TenantMemberships.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Users.Queries;

/// <summary>
/// Lists users who are members of the current tenant (active membership),
/// including their template access within the tenant catalogue.
/// </summary>
public sealed record GetTenantUsersQuery
    : IRequest<Result<IReadOnlyCollection<TenantUserDto>>>;

/// <summary>
/// Handles <see cref="GetTenantUsersQuery"/>.
/// </summary>
public sealed class GetTenantUsersQueryHandler(
    IEaRepository<TenantMembership> membershipRepository,
    IEaRepository<Template> templateRepository,
    ITenantTemplateCatalogue tenantTemplateCatalogue,
    ITenantContextAccessor tenantContextAccessor,
    IPermissionCheckerService permissionCheckerService)
    : IRequestHandler<GetTenantUsersQuery, Result<IReadOnlyCollection<TenantUserDto>>>
{
    public async Task<Result<IReadOnlyCollection<TenantUserDto>>> Handle(
        GetTenantUsersQuery request,
        CancellationToken cancellationToken)
    {
        if (!permissionCheckerService.CanManageUsers())
            return Result<IReadOnlyCollection<TenantUserDto>>.Forbid("Only administrators can list tenant users");

        var currentTenant = tenantContextAccessor.CurrentTenant;
        if (currentTenant is null)
            return Result<IReadOnlyCollection<TenantUserDto>>.Forbid("Tenant context is required");

        var memberships = await new GetActiveTenantMembershipsWithUsersQueryObject(currentTenant.Id)
            .Apply(membershipRepository.Query())
            .ToListAsync(cancellationToken);

        if (memberships.Count == 0)
            return Result<IReadOnlyCollection<TenantUserDto>>.Success(Array.Empty<TenantUserDto>());

        var catalogueIds = await tenantTemplateCatalogue.GetTemplateIdsAsync(cancellationToken);
        var catalogueSet = catalogueIds.ToHashSet();

        var templates = catalogueIds.Count == 0
            ? new List<Template>()
            : await new GetTemplatesByIdsQueryObject(catalogueIds)
                .Apply(templateRepository.Query().AsNoTracking())
                .ToListAsync(cancellationToken);

        var templateLookup = templates.ToDictionary(
            t => t.Id!.Value,
            t => t);

        var result = memberships
            .Where(m => m.User is not null)
            .Select(m =>
            {
                var user = m.User!;
                var roleName = m.Role?.Name
                    ?? RoleNames.FromRoleId(m.RoleId.Value)
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
