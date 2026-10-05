using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.ReportingExport.Queries;

public sealed record GetReportingExportPolicyQuery(Guid TenantId, Guid TemplateId)
    : IRequest<Result<ReportingExportPolicyDto>>;

/// <summary>Every field of a template with whether its answers are exported to reporting.</summary>
public sealed class GetReportingExportPolicyQueryHandler(IReportingExportAccess access, IPrismExportControlClient prism)
    : IRequestHandler<GetReportingExportPolicyQuery, Result<ReportingExportPolicyDto>>
{
    public async Task<Result<ReportingExportPolicyDto>> Handle(GetReportingExportPolicyQuery request, CancellationToken cancellationToken)
    {
        if (await access.CheckAsync(request.TenantId, request.TemplateId, cancellationToken) is { } denial)
        {
            return denial.As<ReportingExportPolicyDto>();
        }

        return await prism.GetPolicyAsync(request.TenantId, request.TemplateId, cancellationToken);
    }
}
