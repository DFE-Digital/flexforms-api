using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GovUK.Dfe.FlexForms.Application.Prism.Queries;

/// <summary>
/// Pages through the current tenant's applications, including deleted ones, for Prism backfill and reconciliation.
/// </summary>
/// <param name="ModifiedSince">Only applications changed at or after this instant (LastModifiedOn, else CreatedOn).</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Rows per page, capped at <see cref="ListPrismApplicationsQueryHandler.MaxPageSize"/>.</param>
public sealed record ListPrismApplicationsQuery(DateTime? ModifiedSince, int Page, int PageSize)
    : IRequest<Result<PrismApplicationPageDto>>;

public sealed class ListPrismApplicationsQueryHandler(
    IEaRepository<Domain.Entities.Application> applicationRepo,
    ITenantTemplateResolver tenantTemplateResolver)
    : IRequestHandler<ListPrismApplicationsQuery, Result<PrismApplicationPageDto>>
{
    public const int MaxPageSize = 1000;

    public async Task<Result<PrismApplicationPageDto>> Handle(
        ListPrismApplicationsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.Page < 1)
            return Result<PrismApplicationPageDto>.Validation("Page must be 1 or greater");

        if (request.PageSize is < 1 or > MaxPageSize)
            return Result<PrismApplicationPageDto>.Validation($"PageSize must be between 1 and {MaxPageSize}");

        var templateIds = await tenantTemplateResolver.GetTemplateIdsForCurrentTenantAsync(cancellationToken);
        if (templateIds.Count == 0)
            return Result<PrismApplicationPageDto>.Success(new PrismApplicationPageDto([], request.Page, request.PageSize, false));

        var query = applicationRepo.Query()
            .AsNoTracking()
            .Where(a => templateIds.Contains(a.TemplateVersion!.TemplateId));

        if (request.ModifiedSince is { } since)
            query = query.Where(a => (a.LastModifiedOn ?? a.CreatedOn) >= since);

        // Fetch one extra row to detect another page without a COUNT.
        var rows = await query
            .OrderBy(a => a.CreatedOn)
            .ThenBy(a => a.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize + 1)
            .Select(a => new
            {
                a.Id,
                a.SourceRevision,
                a.Status,
                a.DeletedOn,
                LastChangedOn = a.LastModifiedOn ?? a.CreatedOn
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Take(request.PageSize)
            .Select(r => new PrismApplicationSummaryDto(
                r.Id!.Value,
                r.SourceRevision,
                r.Status,
                r.Status == ApplicationStatus.Deleted || r.DeletedOn is not null,
                r.LastChangedOn))
            .ToList();

        return Result<PrismApplicationPageDto>.Success(
            new PrismApplicationPageDto(items, request.Page, request.PageSize, rows.Count > request.PageSize));
    }
}
