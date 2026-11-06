using Asp.Versioning;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.FlexForms.Application.Applications.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace GovUK.Dfe.FlexForms.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/integrations")]
public class IntegrationsController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Records an external tenant function's validation result for an uploaded file.
    /// </summary>
    /// <remarks>
    /// <c>message</c> may be Markdown (up to 1000 characters; longer text is truncated). FlexForms Web
    /// renders it on the upload page, the preview submit check and the live banner: paragraphs, line
    /// breaks, <c>**bold**</c>, <c>*italics*</c>, inline code, bullet and numbered lists, and https links. Raw HTML
    /// and headings are not rendered. The text is stored as sent.
    /// </remarks>
    [HttpPost("files/{fileId:guid}/validation-result")]
    [Authorize(Policy = "CanRecordFileValidation")]
    [SwaggerResponse(200, "Validation result recorded.", typeof(UploadDto))]
    [SwaggerResponse(400, "Invalid request data.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized - no valid machine credential.", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Caller does not have FileValidation Write permission.", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "File not found.", typeof(ExceptionResponse))]
    [SwaggerResponse(409, "File does not require validation or the application is already submitted.", typeof(ExceptionResponse))]
    [SwaggerResponse(429, "Too Many Requests.", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    public async Task<IActionResult> RecordFileValidationResultAsync(
        [FromRoute] Guid fileId,
        [FromBody] RecordFileValidationResultRequest request,
        CancellationToken cancellationToken)
    {
        var command = new RecordFileValidationResultCommand(
            fileId,
            request.IsValid,
            request.Message,
            request.CorrelationId,
            request.Source);

        var result = await sender.Send(command, cancellationToken);
        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status200OK
        };
    }
}
