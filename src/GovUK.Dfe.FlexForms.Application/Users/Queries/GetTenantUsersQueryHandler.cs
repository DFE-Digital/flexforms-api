using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Common.QueriesObjects;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Templates.QueryObjects;
using GovUK.Dfe.FlexForms.Application.TenantMemberships.QueryObjects;
using GovUK.Dfe.FlexForms.Application.Users.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Users.Queries;

/// <summary>
/// Lists users who are members of the current tenant (active membership),
/// including their template access within the tenant catalogue.
/// </summary>
public sealed record GetTenantUsersQuery(
    int PageNumber = 1,
    int PageSize = 10,
    Guid? UserId = null,
    string? Email = null)
    : IRequest<Result<PagedResult<TenantUserDto>>>
{
    public const int DefaultPageSize = 10;

    public const int MaxPageSize = 100;
}

/// <summary>
/// Handles <see cref="GetTenantUsersQuery"/>.
/// </summary>
public sealed class GetTenantUsersQueryHandler(
    IEaRepository<TenantMembership> membershipRepository,
    IEaRepository<Permission> permissionRepository,
    IEaRepository<Template> templateRepository,
    ITenantTemplateCatalogue tenantTemplateCatalogue,
    ITenantContextAccessor tenantContextAccessor,
    IPermissionCheckerService permissionCheckerService)
    : IRequestHandler<GetTenantUsersQuery, Result<PagedResult<TenantUserDto>>>
{
    public async Task<Result<PagedResult<TenantUserDto>>> Handle(
        GetTenantUsersQuery request,
        CancellationToken cancellationToken)
    {
        if (!permissionCheckerService.CanManageUsers())
            return Result<PagedResult<TenantUserDto>>.Forbid("Only administrators can list tenant users");

        var currentTenant = tenantContextAccessor.CurrentTenant;
        if (currentTenant is null)
            return Result<PagedResult<TenantUserDto>>.Forbid("Tenant context is required");

        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize, 1, GetTenantUsersQuery.MaxPageSize);

        var membershipQuery = new GetActiveTenantMembershipsForDirectoryQueryObject(
                currentTenant.Id,
                request.UserId is null ? null : new UserId(request.UserId.Value),
                request.Email)
            .Apply(membershipRepository.Query());

        var totalCount = await membershipQuery.CountAsync(cancellationToken);
        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)pageSize);

        if (totalPages > 0 && pageNumber > totalPages)
            pageNumber = totalPages;

        var memberships = totalCount == 0
            ? []
            : await new PagingQuery<TenantMembership>(pageNumber - 1, pageSize)
                .Apply(membershipQuery)
                .ToListAsync(cancellationToken);

        if (memberships.Count == 0)
        {
            return Result<PagedResult<TenantUserDto>>.Success(new PagedResult<TenantUserDto>
            {
                Items = [],
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalPages = totalPages
            });
        }

        var catalogueIds = await tenantTemplateCatalogue.GetTemplateIdsAsync(cancellationToken);
        var catalogueSet = catalogueIds.ToHashSet();

        var templates = catalogueIds.Count == 0
            ? new List<Template>()
            : await new GetTemplatesByIdsQueryObject(catalogueIds)
                .Apply(templateRepository.Query().AsNoTracking())
                .ToListAsync(cancellationToken);

        var templateLookup = templates.ToDictionary(t => t.Id!.Value, t => t);

        var userIds = memberships
            .Where(m => m.User?.Id is not null)
            .Select(m => m.User!.Id!)
            .Distinct()
            .ToList();

        var templatePermissions = await new GetTemplatePermissionsForUsersQueryObject(userIds)
            .Apply(permissionRepository.Query())
            .ToListAsync(cancellationToken);

        var templateIdsByUser = templatePermissions
            .GroupBy(p => p.UserId)
            .ToDictionary(
                g => g.Key,
                g => UserTemplateIds(g, catalogueSet));

        var items = memberships
            .Where(m => m.User is not null)
            .Select(m =>
            {
                var user = m.User!;
                var roleName = m.Role?.Name
                    ?? RoleNames.FromRoleId(m.RoleId.Value)
                    ?? string.Empty;

                templateIdsByUser.TryGetValue(user.Id!, out var userTemplateIds);

                return new TenantUserDto
                {
                    UserId = user.Id!.Value,
                    Name = user.Name,
                    Email = user.Email,
                    Role = roleName,
                    Templates = (userTemplateIds ?? [])
                        .Select(templateId =>
                        {
                            templateLookup.TryGetValue(templateId.Value, out var template);
                            return new TenantUserTemplateDto
                            {
                                TemplateId = templateId.Value,
                                TemplateName = template?.Name ?? templateId.Value.ToString(),
                                IsLive = template?.IsLive ?? false
                            };
                        })
                        .OrderBy(t => t.TemplateName)
                        .ToList()
                };
            })
            .ToList();

        return Result<PagedResult<TenantUserDto>>.Success(new PagedResult<TenantUserDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalPages = totalPages
        });
    }

    private static IReadOnlyList<TemplateId> UserTemplateIds(
        IEnumerable<Permission> permissions,
        HashSet<TemplateId> catalogueSet)
    {
        return permissions
            .Select(TryParseTemplateId)
            .Where(id => id is not null && catalogueSet.Contains(id))
            .Select(id => id!)
            .Distinct()
            .ToList();
    }

    private static TemplateId? TryParseTemplateId(Permission permission)
    {
        if (!Guid.TryParse(permission.ResourceKey, out var id) || id == Guid.Empty)
            return null;

        return new TemplateId(id);
    }
}
