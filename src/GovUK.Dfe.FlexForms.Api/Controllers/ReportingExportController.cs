using Asp.Versioning;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.FlexForms.Application.ReportingExport.Commands;
using GovUK.Dfe.FlexForms.Application.ReportingExport.Queries;
using GovUK.Dfe.FlexForms.Infrastructure.Security;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace GovUK.Dfe.FlexForms.Api.Controllers;

/// <summary>
/// Lets tenant admins decide which answers are exported to reporting (Prism): the default for fields nobody has
/// decided about, per tenant or per template, and Allow/Deny decisions per field. The policy lives in Prism; these
/// endpoints check the caller is an interactive admin of the tenant and forward the change, which starts a refresh
/// of the tenant's exported data.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/admin/tenants/{tenantId:guid}/reporting-export")]
public class ReportingExportController(ISender sender) : ControllerBase
{
    /// <summary>The tenant-wide default for fields without a decision.</summary>
    [HttpGet("default")]
    [Authorize(Policy = AuthConstants.TenantAdminUserPolicy)]
    [SwaggerResponse(200, "The tenant default.", typeof(ReportingExportDefaultDto))]
    [SwaggerResponse(400, "Reporting is unavailable or not set up.", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Forbidden - interactive Admin of own tenant required.", typeof(ExceptionResponse))]
    public async Task<IActionResult> GetTenantReportingExportDefaultAsync(Guid tenantId, CancellationToken cancellationToken)
        => Map(await sender.Send(new GetReportingExportDefaultQuery(tenantId, null), cancellationToken));

    /// <summary>Sets the tenant-wide default. A change starts a refresh of the tenant's exported data.</summary>
    [HttpPut("default")]
    [Authorize(Policy = AuthConstants.TenantAdminUserPolicy)]
    [SwaggerResponse(200, "The outcome.", typeof(ReportingExportChangeResultDto))]
    [SwaggerResponse(400, "Invalid request, or reporting is unavailable.", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Forbidden - interactive Admin of own tenant required.", typeof(ExceptionResponse))]
    public async Task<IActionResult> UpdateTenantReportingExportDefaultAsync(
        Guid tenantId,
        [FromBody] UpdateReportingExportDefaultRequest request,
        CancellationToken cancellationToken)
        => Map(await sender.Send(new UpdateReportingExportDefaultCommand(tenantId, null, request.Mode, request.Reason), cancellationToken));

    /// <summary>Every field of a template with whether its answers are exported, and the default in force.</summary>
    [HttpGet("templates/{templateId:guid}")]
    [Authorize(Policy = AuthConstants.TenantAdminUserPolicy)]
    [SwaggerResponse(200, "The template's fields.", typeof(ReportingExportPolicyDto))]
    [SwaggerResponse(400, "Reporting is unavailable or not set up.", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Forbidden - interactive Admin of own tenant required.", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "Template not found, or its fields are not known to reporting yet.", typeof(ExceptionResponse))]
    public async Task<IActionResult> GetTemplateReportingExportPolicyAsync(Guid tenantId, Guid templateId, CancellationToken cancellationToken)
        => Map(await sender.Send(new GetReportingExportPolicyQuery(tenantId, templateId), cancellationToken));

    /// <summary>Allows or denies fields of a template. Fields left out keep their decision.</summary>
    [HttpPut("templates/{templateId:guid}/decisions")]
    [Authorize(Policy = AuthConstants.TenantAdminUserPolicy)]
    [SwaggerResponse(200, "The outcome.", typeof(ReportingExportChangeResultDto))]
    [SwaggerResponse(400, "Invalid decisions, unknown fields, or reporting is unavailable.", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Forbidden - interactive Admin of own tenant required.", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "Template not found.", typeof(ExceptionResponse))]
    public async Task<IActionResult> UpdateTemplateReportingExportDecisionsAsync(
        Guid tenantId,
        Guid templateId,
        [FromBody] UpdateReportingExportDecisionsRequest request,
        CancellationToken cancellationToken)
        => Map(await sender.Send(new UpdateReportingExportDecisionsCommand(tenantId, templateId, request.Decisions), cancellationToken));

    /// <summary>The template's own default and the default in force for it.</summary>
    [HttpGet("templates/{templateId:guid}/default")]
    [Authorize(Policy = AuthConstants.TenantAdminUserPolicy)]
    [SwaggerResponse(200, "The template default.", typeof(ReportingExportDefaultDto))]
    [SwaggerResponse(400, "Reporting is unavailable or not set up.", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Forbidden - interactive Admin of own tenant required.", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "Template not found.", typeof(ExceptionResponse))]
    public async Task<IActionResult> GetTemplateReportingExportDefaultAsync(Guid tenantId, Guid templateId, CancellationToken cancellationToken)
        => Map(await sender.Send(new GetReportingExportDefaultQuery(tenantId, templateId), cancellationToken));

    /// <summary>Overrides the tenant default for one template, or sets it back to Inherit.</summary>
    [HttpPut("templates/{templateId:guid}/default")]
    [Authorize(Policy = AuthConstants.TenantAdminUserPolicy)]
    [SwaggerResponse(200, "The outcome.", typeof(ReportingExportChangeResultDto))]
    [SwaggerResponse(400, "Invalid request, or reporting is unavailable.", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Forbidden - interactive Admin of own tenant required.", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "Template not found.", typeof(ExceptionResponse))]
    public async Task<IActionResult> UpdateTemplateReportingExportDefaultAsync(
        Guid tenantId,
        Guid templateId,
        [FromBody] UpdateReportingExportDefaultRequest request,
        CancellationToken cancellationToken)
        => Map(await sender.Send(new UpdateReportingExportDefaultCommand(tenantId, templateId, request.Mode, request.Reason), cancellationToken));

    /// <summary>Progress of the refresh started by a change.</summary>
    [HttpGet("refreshes/{refreshId:guid}")]
    [Authorize(Policy = AuthConstants.TenantAdminUserPolicy)]
    [SwaggerResponse(200, "The refresh.", typeof(ReportingExportRefreshDto))]
    [SwaggerResponse(400, "Reporting is unavailable or not set up.", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Forbidden - interactive Admin of own tenant required.", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "Refresh not found.", typeof(ExceptionResponse))]
    public async Task<IActionResult> GetReportingExportRefreshAsync(Guid tenantId, Guid refreshId, CancellationToken cancellationToken)
        => Map(await sender.Send(new GetReportingExportRefreshQuery(tenantId, refreshId), cancellationToken));

    private static IActionResult Map<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
        }

        var statusCode = result.ErrorCode switch
        {
            DomainErrorCode.Forbidden => StatusCodes.Status403Forbidden,
            DomainErrorCode.NotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status400BadRequest,
        };
        return new ObjectResult(new ExceptionResponse
        {
            StatusCode = statusCode,
            Message = result.Error ?? "Request failed",
            ExceptionType = result.ErrorCode?.ToString() ?? "Error",
        })
        {
            StatusCode = statusCode,
        };
    }
}
