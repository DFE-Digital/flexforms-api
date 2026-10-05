using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.ReportingExport.Queries;

/// <summary>The tenant default when <see cref="TemplateId"/> is null, otherwise the template's.</summary>
public sealed record GetReportingExportDefaultQuery(Guid TenantId, Guid? TemplateId)
    : IRequest<Result<ReportingExportDefaultDto>>;

public sealed class GetReportingExportDefaultQueryHandler(IReportingExportAccess access, IPrismExportControlClient prism)
    : IRequestHandler<GetReportingExportDefaultQuery, Result<ReportingExportDefaultDto>>
{
    public async Task<Result<ReportingExportDefaultDto>> Handle(GetReportingExportDefaultQuery request, CancellationToken cancellationToken)
    {
        if (await access.CheckAsync(request.TenantId, request.TemplateId, cancellationToken) is { } denial)
        {
            return denial.As<ReportingExportDefaultDto>();
        }

        return await prism.GetDefaultAsync(request.TenantId, request.TemplateId, cancellationToken);
    }
}
