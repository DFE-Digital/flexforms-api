using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.ReportingExport.Queries;

public sealed record GetReportingExportRefreshQuery(Guid TenantId, Guid RefreshId)
    : IRequest<Result<ReportingExportRefreshDto>>;

/// <summary>Progress of the refresh that re-exports the tenant's applications after a change.</summary>
public sealed class GetReportingExportRefreshQueryHandler(IReportingExportAccess access, IPrismExportControlClient prism)
    : IRequestHandler<GetReportingExportRefreshQuery, Result<ReportingExportRefreshDto>>
{
    public async Task<Result<ReportingExportRefreshDto>> Handle(GetReportingExportRefreshQuery request, CancellationToken cancellationToken)
    {
        if (await access.CheckAsync(request.TenantId, null, cancellationToken) is { } denial)
        {
            return denial.As<ReportingExportRefreshDto>();
        }

        return await prism.GetRefreshAsync(request.TenantId, request.RefreshId, cancellationToken);
    }
}
