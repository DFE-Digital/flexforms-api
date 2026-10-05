using System.Security.Claims;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace GovUK.Dfe.FlexForms.Application.ReportingExport.Commands;

/// <summary>Sets the tenant default when <see cref="TemplateId"/> is null, otherwise the template's.</summary>
public sealed record UpdateReportingExportDefaultCommand(
    Guid TenantId,
    Guid? TemplateId,
    ReportingExportModeSetting Mode,
    string? Reason)
    : IRequest<Result<ReportingExportChangeResultDto>>;

/// <summary>
/// Changes what happens to undecided fields. A change starts a refresh of the tenant's exported data.
/// Callers must be interactive tenant Admin or SuperAdmin of the tenant resolved for the current request.
/// </summary>
public sealed class UpdateReportingExportDefaultCommandHandler(
    IReportingExportPolicyService reportingExportPolicyService,
    ITenantContextAccessor tenantContextAccessor,
    IPermissionCheckerService permissionChecker,
    ITenantTemplateResolver tenantTemplateResolver,
    IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<UpdateReportingExportDefaultCommand, Result<ReportingExportChangeResultDto>>
{
    public async Task<Result<ReportingExportChangeResultDto>> Handle(
        UpdateReportingExportDefaultCommand request,
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

        if (request.TemplateId is { } templateId
            && !await tenantTemplateResolver.IsTemplateInCurrentTenantAsync(new TemplateId(templateId), cancellationToken))
        {
            return Result<ReportingExportChangeResultDto>.NotFound("Template not found.");
        }

        try
        {
            var result = await reportingExportPolicyService.ChangeDefaultAsync(
                request.TenantId,
                request.TemplateId,
                new UpdateReportingExportDefaultRequest { Mode = request.Mode, Reason = request.Reason },
                ResolveActorEmail(),
                cancellationToken);

            return result is null
                ? Result<ReportingExportChangeResultDto>.NotFound("Template not found.")
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
