using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.ReportingExport.Queries;

public sealed record GetReportingExportPolicyQuery(Guid TenantId, Guid TemplateId)
    : IRequest<Result<ReportingExportPolicyDto>>;

/// <summary>
/// Every field of a template with whether its answers are exported to reporting.
/// Callers must be interactive tenant Admin or SuperAdmin of the tenant resolved for the current request.
/// </summary>
public sealed class GetReportingExportPolicyQueryHandler(
    IReportingExportPolicyService reportingExportPolicyService,
    ITenantContextAccessor tenantContextAccessor,
    IPermissionCheckerService permissionChecker,
    ITenantTemplateResolver tenantTemplateResolver)
    : IRequestHandler<GetReportingExportPolicyQuery, Result<ReportingExportPolicyDto>>
{
    public async Task<Result<ReportingExportPolicyDto>> Handle(
        GetReportingExportPolicyQuery request,
        CancellationToken cancellationToken)
    {
        if (!permissionChecker.IsInteractiveTenantAdmin())
        {
            return Result<ReportingExportPolicyDto>.Forbid(
                "Only interactive tenant administrators can manage reporting export.");
        }

        var currentTenant = tenantContextAccessor.CurrentTenant;
        if (currentTenant is null || currentTenant.Id != request.TenantId)
        {
            return Result<ReportingExportPolicyDto>.Forbid(
                "Administrators can only manage reporting export for their own tenant.");
        }

        if (!await tenantTemplateResolver.IsTemplateInCurrentTenantAsync(new TemplateId(request.TemplateId), cancellationToken))
        {
            return Result<ReportingExportPolicyDto>.NotFound("Template not found.");
        }

        try
        {
            var policy = await reportingExportPolicyService.GetPolicyAsync(request.TenantId, request.TemplateId, cancellationToken);

            return policy is null
                ? Result<ReportingExportPolicyDto>.NotFound(
                    "This template's fields are not known to reporting yet. They appear once the template is published or an application is saved.")
                : Result<ReportingExportPolicyDto>.Success(policy);
        }
        catch (InvalidOperationException ex)
        {
            return Result<ReportingExportPolicyDto>.Failure(ex.Message);
        }
    }
}
