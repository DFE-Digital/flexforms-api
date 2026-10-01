using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.Prism.Queries;

/// <summary>
/// Authoritative list of tenants for Prism reconciliation and backfill. Does not require tenant context.
/// </summary>
public sealed record GetPrismTenantsQuery : IRequest<Result<IReadOnlyList<PrismTenantDto>>>;

public sealed class GetPrismTenantsQueryHandler(ITenantConfigurationProvider tenantConfigurationProvider)
    : IRequestHandler<GetPrismTenantsQuery, Result<IReadOnlyList<PrismTenantDto>>>
{
    public Task<Result<IReadOnlyList<PrismTenantDto>>> Handle(
        GetPrismTenantsQuery request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PrismTenantDto> tenants = tenantConfigurationProvider.GetAllTenants()
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => new PrismTenantDto(t.Id, t.Name))
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<PrismTenantDto>>.Success(tenants));
    }
}
