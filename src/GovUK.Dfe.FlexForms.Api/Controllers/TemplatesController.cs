using Asp.Versioning;
using GovUK.Dfe.FlexForms.Application.Common.Exceptions;
using GovUK.Dfe.FlexForms.Application.Templates.Commands;
using GovUK.Dfe.FlexForms.Application.Templates.Queries;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Http.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace GovUK.Dfe.FlexForms.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/[controller]")]
public class TemplatesController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Returns templates in the current tenant that the caller can access.
    /// Admins see the full tenant catalogue (including non-live); other users see live templates they have permission for.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "CanListTemplates")]
    [SwaggerResponse(200, "Accessible templates for the current tenant.", typeof(IReadOnlyCollection<TemplateDto>))]
    [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Access denied.", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    public async Task<IActionResult> GetAccessibleTemplatesAsync(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetAccessibleTemplatesQuery(), cancellationToken);

        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status200OK
        };
    }

    /// <summary>
/// Creates a new template in the current tenant. Admin or Template:Any:Manage.
/// The creating user is granted Read/Write template permission.
/// </summary>
    [HttpPost]
    [Authorize(Policy = "CanCreateTemplate")]
    [SwaggerResponse(201, "Template created successfully.", typeof(TemplateDto))]
    [SwaggerResponse(400, "Invalid request data.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Access denied.", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    [SwaggerResponse(429, "Too Many Requests.", typeof(ExceptionResponse))]
    public async Task<IActionResult> CreateTemplateAsync(
        [FromBody] CreateTemplateRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
            throw new BadRequestException("Invalid request data.");

        var command = new CreateTemplateCommand(
            request.Name,
            request.InitialVersionNumber,
            request.JsonSchema);

        var result = await sender.Send(command, cancellationToken);

        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status201Created
        };
    }

    /// <summary>
    /// Returns the latest template schema for the specified template name if the user has access.
    /// </summary>
    [HttpGet("{templateId}/schema")]
    [SwaggerResponse(200, "The latest template schema.", typeof(TemplateSchemaDto))]
    [SwaggerResponse(400, "Request was invalid or template not found.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Access denied.", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "Template not found.", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    [Authorize(Policy = "CanReadTemplate")]
    public async Task<IActionResult> GetLatestTemplateSchemaAsync(
        [FromRoute] Guid templateId, CancellationToken cancellationToken)
    {
        var query = new GetLatestTemplateSchemaQuery(templateId);
        var result = await sender.Send(query, cancellationToken);

        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status200OK
        };
    }

    /// <summary>
    /// Get custom application statuses for a template.
    /// </summary>
    [HttpGet("{templateId}/custom-statuses")]
    [SwaggerResponse(200, "Custom statuses returned.", typeof(IReadOnlyCollection<CustomApplicationStatusDto>))]
    [Authorize(Policy = "CanReadTemplate")]
    public async Task<IActionResult> GetCustomApplicationStatusesAsync([FromRoute] Guid templateId, CancellationToken cancellationToken)
    {
        var query = new GetCustomApplicationStatusesQuery(templateId);
        var result = await sender.Send(query, cancellationToken);

        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status200OK
        };
    }

    /// <summary>
    /// Create or update a custom application status for a template.
    /// If a CustomApplicationStatus for the TemplateId and ApplicationStatus exists, update its label; otherwise create one.
    /// Returns the created or updated CustomApplicationStatus.
    /// </summary>
    [HttpPost("{templateId}/custom-statuses")]
    [SwaggerResponse(201, "Custom status created/updated.", typeof(CustomApplicationStatusDto))]
    [SwaggerResponse(400, "Invalid request data.", typeof(ExceptionResponse))]
    [Authorize(Policy = "CanCreateTemplate")]
    public async Task<IActionResult> CreateCustomApplicationStatusAsync([FromRoute] Guid templateId, [FromBody] CustomApplicationStatusRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
            throw new BadRequestException("Invalid request data.");

        var command = new UpdateCustomApplicationStatusCommand(templateId, request.ApplicationStatus, request.Label);
        var result = await sender.Send(command, cancellationToken);

        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status201Created
        };
    }

    /// <summary>
/// Sets whether a template is live for end users. Admin or Template:Any:Manage
/// (or Template:{id}:Manage for that template).
/// </summary>
    [HttpPut("{templateId}/live")]
    [Authorize(Policy = "CanCreateTemplate")]
    [SwaggerResponse(200, "Template live status updated.", typeof(TemplateDto))]
    [SwaggerResponse(400, "Invalid request data.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Access denied.", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "Template not found.", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    public async Task<IActionResult> SetTemplateLiveAsync(
        [FromRoute] Guid templateId,
        [FromBody] SetTemplateLiveRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
            throw new BadRequestException("Invalid request data.");

        var result = await sender.Send(new SetTemplateLiveCommand(templateId, request.IsLive), cancellationToken);

        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status200OK
        };
    }

    /// <summary>
    /// Creates a new schema version for the specified template.
    /// </summary>
    [HttpPost("{templateId}/versions")]
    [SwaggerResponse(201, "Template version created successfully.", typeof(TemplateSchemaDto))]
    [SwaggerResponse(400, "Invalid request data.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Access denied.", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "Template not found.", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    [SwaggerResponse(429, "Too Many Requests.", typeof(ExceptionResponse))]
    [Authorize(Policy = "CanCreateTemplate")]
    public async Task<IActionResult> CreateTemplateVersionAsync(
        [FromRoute] Guid templateId,
        [FromBody] CreateTemplateVersionRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CreateTemplateVersionCommand(templateId, request.VersionNumber, request.JsonSchema);
        var result = await sender.Send(command, cancellationToken);

        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status201Created
        };
    }
}
