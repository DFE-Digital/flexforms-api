using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.ReportingExport.Queries;

public sealed record GetReportingExportRefreshQuery(Guid TenantId, Guid RefreshId)
    : IRequest<Result<ReportingExportRefreshDto>>;

/// <summary>
/// Progress of the refresh that re-exports the tenant's applications after a change.
/// Callers must be interactive tenant Admin or SuperAdmin of the tenant resolved for the current request.
/// </summary>
public sealed class GetReportingExportRefreshQueryHandler(
    IReportingExportPolicyService reportingExportPolicyService,
    ITenantContextAccessor tenantContextAccessor,
    IPermissionCheckerService permissionChecker)
    : IRequestHandler<GetReportingExportRefreshQuery, Result<ReportingExportRefreshDto>>
{
    public async Task<Result<ReportingExportRefreshDto>> Handle(
        GetReportingExportRefreshQuery request,
        CancellationToken cancellationToken)
    {
        if (!permissionChecker.IsInteractiveTenantAdmin())
        {
            return Result<ReportingExportRefreshDto>.Forbid(
                "Only interactive tenant administrators can manage reporting export.");
        }

        var currentTenant = tenantContextAccessor.CurrentTenant;
        if (currentTenant is null || currentTenant.Id != request.TenantId)
        {
            return Result<ReportingExportRefreshDto>.Forbid(
                "Administrators can only manage reporting export for their own tenant.");
        }

        try
        {
            var refresh = await reportingExportPolicyService.GetRefreshAsync(request.TenantId, request.RefreshId, cancellationToken);

            return refresh is null
                ? Result<ReportingExportRefreshDto>.NotFound("Refresh not found.")
                : Result<ReportingExportRefreshDto>.Success(refresh);
        }
        catch (InvalidOperationException ex)
        {
            return Result<ReportingExportRefreshDto>.Failure(ex.Message);
        }
    }
}
