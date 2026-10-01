using Asp.Versioning;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.FlexForms.Application.Prism.Queries;
using GovUK.Dfe.FlexForms.Infrastructure.Security;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace GovUK.Dfe.FlexForms.Api.Controllers;

/// <summary>
/// Read-only endpoints for the Prism analytics projector. Platform bearer tokens with the
/// <c>Prism.Read</c> app role only. Every endpoint except <c>tenants</c> is scoped by <c>X-Tenant-ID</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/internal/prism")]
[Authorize(Policy = PlatformConstants.PlatformPrismReadPolicy)]
public class InternalPrismController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Returns every configured tenant. Does not require tenant context.
    /// </summary>
    [HttpGet("tenants")]
    [SwaggerResponse(200, "Configured tenants.", typeof(IReadOnlyList<PrismTenantDto>))]
    [SwaggerResponse(401, "Unauthorized.", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Missing Prism.Read app role.", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    public async Task<IActionResult> GetPrismTenantsAsync(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetPrismTenantsQuery(), cancellationToken);
        return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
    }

    /// <summary>
    /// Returns the current source state of an application, including deleted applications.
    /// </summary>
    [HttpGet("applications/{applicationId:guid}/current")]
    [SwaggerResponse(200, "Current application state.", typeof(PrismApplicationStateDto))]
    [SwaggerResponse(401, "Unauthorized.", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Missing Prism.Read app role.", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "Application not found in this tenant.", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    public async Task<IActionResult> GetPrismApplicationStateAsync(
        [FromRoute] Guid applicationId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetPrismApplicationStateQuery(applicationId), cancellationToken);
        return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
    }

    /// <summary>
    /// Returns one immutable response version.
    /// </summary>
    [HttpGet("responses/{responseId:guid}")]
    [SwaggerResponse(200, "Response version.", typeof(PrismResponseDto))]
    [SwaggerResponse(401, "Unauthorized.", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Missing Prism.Read app role.", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "Response not found in this tenant.", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    public async Task<IActionResult> GetPrismResponseAsync(
        [FromRoute] Guid responseId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetPrismResponseQuery(responseId), cancellationToken);
        return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
    }

    /// <summary>
    /// Returns an immutable template version and its JSON schema.
    /// </summary>
    [HttpGet("template-versions/{templateVersionId:guid}")]
    [SwaggerResponse(200, "Template version.", typeof(PrismTemplateVersionDto))]
    [SwaggerResponse(401, "Unauthorized.", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Missing Prism.Read app role.", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "Template version not found in this tenant.", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    public async Task<IActionResult> GetPrismTemplateVersionAsync(
        [FromRoute] Guid templateVersionId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetPrismTemplateVersionQuery(templateVersionId), cancellationToken);
        return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
    }

    /// <summary>
    /// Pages through the tenant's applications (including deleted), oldest first.
    /// </summary>
    [HttpGet("applications")]
    [SwaggerResponse(200, "A page of applications.", typeof(PrismApplicationPageDto))]
    [SwaggerResponse(400, "Invalid paging parameters.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized.", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Missing Prism.Read app role.", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    public async Task<IActionResult> ListPrismApplicationsAsync(
        [FromQuery] DateTime? modifiedSince,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 500)
    {
        var result = await sender.Send(new ListPrismApplicationsQuery(modifiedSince, page, pageSize), cancellationToken);
        return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
    }
}
