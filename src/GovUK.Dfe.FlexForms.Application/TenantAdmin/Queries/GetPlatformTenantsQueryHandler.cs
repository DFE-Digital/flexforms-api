using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin.Queries;

public sealed record GetPlatformTenantsQuery : IRequest<Result<GetPlatformTenantsResponse>>;

/// <summary>
/// SuperAdmin-only read-only catalogue of all tenants in the platform config store.
/// </summary>
public sealed class GetPlatformTenantsQueryHandler(
    ITenantConfigurationProvider tenantConfigProvider,
    ITenantConfigurationRefreshState? refreshState,
    IPermissionCheckerService permissionChecker,
    ITenantHostnameResolver hostnameResolver)
    : IRequestHandler<GetPlatformTenantsQuery, Result<GetPlatformTenantsResponse>>
{
    public async Task<Result<GetPlatformTenantsResponse>> Handle(
        GetPlatformTenantsQuery request,
        CancellationToken cancellationToken)
    {
        if (!permissionChecker.IsInteractivePlatformAdmin())
        {
            return Result<GetPlatformTenantsResponse>.Forbid(
                "Only interactive SuperAdmin users can list all platform tenants.");
        }

        var tenants = tenantConfigProvider.GetAllTenants();
        var summaries = new List<PlatformTenantSummaryDto>();

        foreach (var tenant in tenants.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            var hostnames = await hostnameResolver.ListHostnamesForTenantAsync(tenant.Id, cancellationToken);
            var scheme = TenantInteractiveAuthSchemeResolver.ResolveSchemeName(tenant.Settings);

            summaries.Add(new PlatformTenantSummaryDto(
                tenant.Id,
                tenant.Name,
                IsActive: true,
                hostnames,
                tenant.FrontendOrigins ?? [],
                scheme));
        }

        return Result<GetPlatformTenantsResponse>.Success(
            new GetPlatformTenantsResponse(
                tenantConfigProvider.Source,
                summaries.Count,
                refreshState?.LastRefreshedUtc,
                summaries));
    }
}
