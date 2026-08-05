using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin.Queries;

public sealed record ExportTenantConfigurationQuery(Guid TenantId)
    : IRequest<Result<ExportTenantConfigurationDto>>;

public sealed class ExportTenantConfigurationQueryHandler(
    ITenantContextAccessor tenantContextAccessor,
    IPermissionCheckerService permissionChecker,
    ITenantConfigurationPromotion promotionService)
    : IRequestHandler<ExportTenantConfigurationQuery, Result<ExportTenantConfigurationDto>>
{
    public async Task<Result<ExportTenantConfigurationDto>> Handle(
        ExportTenantConfigurationQuery request,
        CancellationToken cancellationToken)
    {
        if (!permissionChecker.IsInteractivePlatformAdmin())
        {
            return Result<ExportTenantConfigurationDto>.Forbid(
                "Only interactive SuperAdmin users can export tenant configuration.");
        }

        var currentTenant = tenantContextAccessor.CurrentTenant;
        if (currentTenant is null || currentTenant.Id != request.TenantId)
        {
            return Result<ExportTenantConfigurationDto>.Forbid(
                "Administrators may only export their own tenant configuration.");
        }

        var export = await promotionService.ExportAsync(request.TenantId, cancellationToken);
        if (export is null)
        {
            return Result<ExportTenantConfigurationDto>.NotFound(
                $"Tenant '{request.TenantId}' was not found.");
        }

        return Result<ExportTenantConfigurationDto>.Success(export);
    }
}
