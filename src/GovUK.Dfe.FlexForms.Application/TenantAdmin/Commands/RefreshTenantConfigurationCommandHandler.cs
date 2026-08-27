using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin.Commands;

public sealed record RefreshTenantConfigurationCommand : IRequest<Result<RefreshTenantConfigurationResponse>>;

/// <summary>
/// Refreshes the in-memory tenant configuration cache.
/// SuperAdmin receives the full tenant catalogue in the response.
/// Tenant Admin may trigger refresh but only receives their own tenant summary
/// (never other tenants' Ids/names).
/// </summary>
public sealed class RefreshTenantConfigurationCommandHandler(
    ITenantConfigurationProvider tenantConfigProvider,
    ITenantContextAccessor tenantContextAccessor,
    IPermissionCheckerService permissionChecker)
    : IRequestHandler<RefreshTenantConfigurationCommand, Result<RefreshTenantConfigurationResponse>>
{
    public async Task<Result<RefreshTenantConfigurationResponse>> Handle(
        RefreshTenantConfigurationCommand request,
        CancellationToken cancellationToken)
    {
        if (!permissionChecker.IsInteractiveTenantAdmin())
        {
            return Result<RefreshTenantConfigurationResponse>.Forbid(
                "Only interactive Admin users can refresh tenant configuration.");
        }

        var currentTenant = tenantContextAccessor.CurrentTenant;
        if (currentTenant is null)
        {
            return Result<RefreshTenantConfigurationResponse>.Forbid(
                "Tenant context is required to refresh tenant configuration.");
        }

        await tenantConfigProvider.RefreshAsync(cancellationToken);

        var isSuperAdmin = permissionChecker.IsInteractivePlatformAdmin();
        IReadOnlyList<TenantSummaryDto> summaries;
        int tenantCount;

        if (isSuperAdmin)
        {
            var tenants = tenantConfigProvider.GetAllTenants();
            summaries = tenants
                .Select(t => new TenantSummaryDto(t.Id, t.Name))
                .ToList()
                .AsReadOnly();
            tenantCount = tenants.Count;
        }
        else
        {
            // Tenant Admin must not learn other tenants' Ids/names from this endpoint.
            summaries = new List<TenantSummaryDto>
            {
                new(currentTenant.Id, currentTenant.Name)
            }.AsReadOnly();
            tenantCount = 1;
        }

        var message = tenantConfigProvider.Source == "AppSettings"
            ? "Tenant configuration is loaded from appsettings. Cache is static."
            : "Tenant configuration refreshed successfully.";

        return Result<RefreshTenantConfigurationResponse>.Success(
            new RefreshTenantConfigurationResponse(message, tenantCount, summaries));
    }
}
