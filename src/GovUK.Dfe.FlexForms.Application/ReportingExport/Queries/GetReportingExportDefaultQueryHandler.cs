using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.ReportingExport.Queries;

/// <summary>The tenant default when <see cref="TemplateId"/> is null, otherwise the template's.</summary>
public sealed record GetReportingExportDefaultQuery(Guid TenantId, Guid? TemplateId)
    : IRequest<Result<ReportingExportDefaultDto>>;

/// <summary>
/// What happens to undecided fields, and where that comes from.
/// Callers must be interactive tenant Admin or SuperAdmin of the tenant resolved for the current request.
/// </summary>
public sealed class GetReportingExportDefaultQueryHandler(
    IReportingExportPolicyService reportingExportPolicyService,
    ITenantContextAccessor tenantContextAccessor,
    IPermissionCheckerService permissionChecker,
    ITenantTemplateResolver tenantTemplateResolver)
    : IRequestHandler<GetReportingExportDefaultQuery, Result<ReportingExportDefaultDto>>
{
    public async Task<Result<ReportingExportDefaultDto>> Handle(
        GetReportingExportDefaultQuery request,
        CancellationToken cancellationToken)
    {
        if (!permissionChecker.IsInteractiveTenantAdmin())
        {
            return Result<ReportingExportDefaultDto>.Forbid(
                "Only interactive tenant administrators can manage reporting export.");
        }

        var currentTenant = tenantContextAccessor.CurrentTenant;
        if (currentTenant is null || currentTenant.Id != request.TenantId)
        {
            return Result<ReportingExportDefaultDto>.Forbid(
                "Administrators can only manage reporting export for their own tenant.");
        }

        if (request.TemplateId is { } templateId
            && !await tenantTemplateResolver.IsTemplateInCurrentTenantAsync(new TemplateId(templateId), cancellationToken))
        {
            return Result<ReportingExportDefaultDto>.NotFound("Template not found.");
        }

        try
        {
            var exportDefault = await reportingExportPolicyService.GetDefaultAsync(request.TenantId, request.TemplateId, cancellationToken);

            return exportDefault is null
                ? Result<ReportingExportDefaultDto>.NotFound("Template not found.")
                : Result<ReportingExportDefaultDto>.Success(exportDefault);
        }
        catch (InvalidOperationException ex)
        {
            return Result<ReportingExportDefaultDto>.Failure(ex.Message);
        }
    }
}
