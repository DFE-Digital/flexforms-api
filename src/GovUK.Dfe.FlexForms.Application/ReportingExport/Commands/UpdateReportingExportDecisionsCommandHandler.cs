using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace GovUK.Dfe.FlexForms.Application.ReportingExport.Commands;

public sealed record UpdateReportingExportDecisionsCommand(
    Guid TenantId,
    Guid TemplateId,
    IReadOnlyList<ReportingExportDecisionRequest> Decisions)
    : IRequest<Result<ReportingExportChangeResultDto>>;

/// <summary>
/// Allows or denies fields of a template. A change starts a refresh of the tenant's exported data.
/// Callers must be interactive tenant Admin or SuperAdmin of the tenant resolved for the current request.
/// </summary>
public sealed class UpdateReportingExportDecisionsCommandHandler(
    IReportingExportPolicyService reportingExportPolicyService,
    ITenantContextAccessor tenantContextAccessor,
    IPermissionCheckerService permissionChecker,
    ITenantTemplateResolver tenantTemplateResolver,
    IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<UpdateReportingExportDecisionsCommand, Result<ReportingExportChangeResultDto>>
{
    public async Task<Result<ReportingExportChangeResultDto>> Handle(
        UpdateReportingExportDecisionsCommand request,
        CancellationToken cancellationToken)
    {
        if (!permissionChecker.IsInteractiveTenantAdmin())
        {
            return Result<ReportingExportChangeResultDto>.Forbid(
                "Only interactive tenant administrators can manage reporting export.");
        }

        var currentTenant = tenantContextAccessor.CurrentTenant;
        if (currentTenant is null || currentTenant.Id != request.TenantId)
        {
            return Result<ReportingExportChangeResultDto>.Forbid(
                "Administrators can only manage reporting export for their own tenant.");
        }

        if (!await tenantTemplateResolver.IsTemplateInCurrentTenantAsync(new TemplateId(request.TemplateId), cancellationToken))
        {
            return Result<ReportingExportChangeResultDto>.NotFound("Template not found.");
        }

        try
        {
            var result = await reportingExportPolicyService.ChangeDecisionsAsync(
                request.TenantId,
                request.TemplateId,
                request.Decisions,
                ResolveActorEmail(),
                cancellationToken);

            return result is null
                ? Result<ReportingExportChangeResultDto>.NotFound(
                    "This template's fields are not known to reporting yet. They appear once the template is published or an application is saved.")
                : Result<ReportingExportChangeResultDto>.Success(result);
        }
        catch (ArgumentException ex)
        {
            return Result<ReportingExportChangeResultDto>.Validation(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Result<ReportingExportChangeResultDto>.Failure(ex.Message);
        }
    }

    private string ResolveActorEmail()
    {
        var user = httpContextAccessor.HttpContext?.User;
        return new[] { user?.FindFirst(ClaimTypes.Email)?.Value, user?.FindFirst("email")?.Value, user?.Identity?.Name }
                   .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
               ?? "unknown";
    }
}
