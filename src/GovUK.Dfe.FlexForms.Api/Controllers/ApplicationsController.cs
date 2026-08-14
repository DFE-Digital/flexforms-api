using Asp.Versioning;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.FlexForms.Api.Models.Applications;
using GovUK.Dfe.FlexForms.Application.Applications.Commands;
using GovUK.Dfe.FlexForms.Application.Applications.Queries;
using GovUK.Dfe.FlexForms.Application.Common.Exceptions;
using GovUK.Dfe.FlexForms.Api.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;

namespace GovUK.Dfe.FlexForms.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/[controller]")]
public class ApplicationsController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Creates a new application with initial response.
    /// </summary>
    [HttpPost]
    [SwaggerResponse(200, "The created application.", typeof(ApplicationDto))]
    [SwaggerResponse(400, "Invalid request data.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Forbidden - user does not have required permissions", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    [SwaggerResponse(429, "Too Many Requests.", typeof(ExceptionResponse))]
    [Authorize(Policy = "CanCreateAnyApplication")]
    public async Task<IActionResult> CreateApplicationAsync(
        [FromBody] CreateApplicationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateApplicationCommand(request.TemplateId, request.InitialResponseBody), cancellationToken);

        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status200OK
        };
    }

    /// <summary>
    /// Adds a new response version to an existing application.
    /// </summary>
    [HttpPost]
    [Route(("{applicationId}/responses"))]
    [SwaggerResponse(201, "Response version created.", typeof(ApplicationResponseDto))]
    [SwaggerResponse(400, "Invalid request data.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized - no valid user token.", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "User does not have permission to update this application.", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "Application not found.", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    [SwaggerResponse(429, "Too Many Requests.", typeof(ExceptionResponse))]
    [Authorize(Policy = "CanUpdateApplication")]
    public async Task<IActionResult> AddApplicationResponseAsync(
        [FromRoute] Guid applicationId,
        [FromBody] AddApplicationResponseRequest request,
        CancellationToken cancellationToken)
    {
        var command = new AddApplicationResponseCommand(applicationId, request.ResponseBody);
        var result = await sender.Send(command, cancellationToken);

        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status201Created
        };
    }

    /// <summary>
    /// Returns a paged list of the current user's own applications in the current tenant.
    /// </summary>
    [HttpGet]
    [Route("/v{version:apiVersion}/me/applications")]
    [SwaggerResponse(200, "A paged list of applications accessible to the user.", typeof(PagedResult<ApplicationDto>))]
    [SwaggerResponse(400, "Invalid request data.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Forbidden - user does not have required permissions", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    [Authorize(Policy = "CanReadAnyApplication")]
    public async Task<IActionResult> GetMyApplicationsAsync(
        CancellationToken cancellationToken,
        [FromQuery] GetMyApplicationsQueryParameters parameters)
    {
        var result = await sender.Send(parameters.ToQuery(), cancellationToken);

        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status200OK
        };
    }

    /// <summary>
    /// Returns a paged list of all applications for the specified template, based on the caller's role (admin or caseworker).
    /// </summary>
    [HttpGet("templates/{templateId:guid}")]
    [SwaggerResponse(200, "A paged list of applications for the template.", typeof(PagedResult<ApplicationDto>))]
    [SwaggerResponse(400, "Invalid request data.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Forbidden - user does not have required permissions", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    [Authorize(Policy = "CanReadAnyApplication")]
    public async Task<IActionResult> GetApplicationsByTemplateAsync(
        [FromRoute] Guid templateId,
        CancellationToken cancellationToken,
        [FromQuery] ApplicationListingQueryParameters parameters)
    {
        var result = await sender.Send(parameters.ToQuery(templateId), cancellationToken);

        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status200OK
        };
    }

    /// <summary>
    /// Returns all applications for the user by {email}.
    /// </summary>
    [HttpGet]
    [Route("/v{version:apiVersion}/Users/{email}/applications")]
    [SwaggerResponse(200, "A paged list of applications for the user.", typeof(PagedResult<ApplicationDto>))]
    [SwaggerResponse(400, "Email cannot be null or empty.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Forbidden - user does not have required permissions", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    [Authorize(Policy = "CanReadAnyApplication")]
    public async Task<IActionResult> GetApplicationsForUserAsync(
        [FromRoute] string email,
        CancellationToken cancellationToken,
        [FromQuery] bool? includeSchema = null,
        [FromQuery] Guid? templateId = null)
    {
        var query = new GetApplicationsForUserQuery(email, includeSchema ?? false, templateId);
        var result = await sender.Send(query, cancellationToken);

        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status200OK
        };
    }

    /// <summary>
    /// Returns application details with its latest response by application reference.
    /// </summary>
    [HttpGet("reference/{applicationReference}")]
    [SwaggerResponse(200, "Application details with latest response.", typeof(ApplicationDto))]
    [SwaggerResponse(400, "Invalid application reference or application not found.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "User does not have permission to read this application", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "Application not found", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    [Authorize(Policy = "CanReadAnyApplication")]
    public async Task<IActionResult> GetApplicationByReferenceAsync(
        [FromRoute] string applicationReference,
        CancellationToken cancellationToken)
    {
        var query = new GetApplicationByReferenceQuery(applicationReference);
        var result = await sender.Send(query, cancellationToken);

        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status200OK
        };
    }

    /// <summary>
    /// Soft deletes an application, changing its status to Deleted.
    /// </summary>
    [HttpPut("{applicationId}/delete")]
    [SwaggerResponse(200, "Application deleted successfully.", typeof(ApplicationDto))]
    [SwaggerResponse(400, "Invalid request data or application not found.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "User does not have permission to delete this application", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "Application not found", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    [SwaggerResponse(429, "Too Many Requests.", typeof(ExceptionResponse))]
    [Authorize(Policy = "CanUpdateApplication")]
    public async Task<IActionResult> DeleteApplicationAsync(
        [FromRoute] Guid applicationId,
        CancellationToken cancellationToken)
    {
        var command = new DeleteApplicationCommand(applicationId);
        var result = await sender.Send(command, cancellationToken);

        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status200OK
        };
    }

    /// <summary>
    /// UnDeletes an application, changing its status to the previous status.
    /// </summary>
    [HttpPut("{applicationId}/un-delete")]
    [SwaggerResponse(200, "Application un-deleted successfully.", typeof(ApplicationDto))]
    [SwaggerResponse(400, "Invalid request data or application not found.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "User does not have permission to un-delete this application", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "Application not found", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    [SwaggerResponse(429, "Too Many Requests.", typeof(ExceptionResponse))]
    [Authorize(Policy = "CanUpdateApplication")]
    public async Task<IActionResult> UnDeleteApplicationAsync(
        [FromRoute] Guid applicationId,
        CancellationToken cancellationToken)
    {
        var command = new UnDeleteApplicationCommand(applicationId);
        var result = await sender.Send(command, cancellationToken);

        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status200OK
        };
    }

    /// <summary>
    /// Submits an application, changing its status to Submitted.
    /// </summary>
    [HttpPost("{applicationId}/submit")]
    [SwaggerResponse(200, "Application submitted successfully.", typeof(ApplicationDto))]
    [SwaggerResponse(400, "Invalid request data or application already submitted.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "User does not have permission to submit this application", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "Application not found", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    [SwaggerResponse(429, "Too Many Requests.", typeof(ExceptionResponse))]
    [Authorize(Policy = "CanUpdateApplication")]
    public async Task<IActionResult> SubmitApplicationAsync(
        [FromRoute] Guid applicationId,
        CancellationToken cancellationToken)
    {
        var command = new SubmitApplicationCommand(applicationId);
        var result = await sender.Send(command, cancellationToken);

        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status200OK
        };
    }

    /// <summary>
    /// Gets all contributors for a specific application.
    /// </summary>
    [HttpGet("{applicationId}/contributors")]
    [SwaggerResponse(200, "List of contributors for the application.", typeof(IReadOnlyCollection<UserDto>))]
    [SwaggerResponse(400, "Invalid request data.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "User does not have permission to read this application", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "Application not found", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    [Authorize(Policy = "CanReadAnyApplication")]
    public async Task<IActionResult> GetContributorsAsync(
        [FromRoute] Guid applicationId,
        CancellationToken cancellationToken,
        [FromQuery] bool? includePermissionDetails = null)
    {
        var query = new GetContributorsForApplicationQuery(applicationId, includePermissionDetails ?? false);
        var result = await sender.Send(query, cancellationToken);

        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status200OK
        };
    }

    /// <summary>
    /// Adds a contributor to an application.
    /// </summary>
    [HttpPost("{applicationId}/contributors")]
    [SwaggerResponse(200, "Contributor added successfully.", typeof(UserDto))]
    [SwaggerResponse(400, "Invalid request data or contributor already exists.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "User does not have permission to manage contributors for this application", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "Application not found", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    [SwaggerResponse(429, "Too Many Requests.", typeof(ExceptionResponse))]
    [Authorize(Policy = "CanUpdateApplication")]
    public async Task<IActionResult> AddContributorAsync(
        [FromRoute] Guid applicationId,
        [FromBody] AddContributorRequest request,
        CancellationToken cancellationToken)
    {
        var command = new AddContributorCommand(applicationId, request.Name, request.Email);
        var result = await sender.Send(command, cancellationToken);

        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status200OK
        };
    }

    /// <summary>
    /// Removes a contributor from an application.
    /// </summary>
    [HttpDelete("{applicationId}/contributors/{userId}")]
    [SwaggerResponse(200, "Contributor removed successfully.")]
    [SwaggerResponse(400, "Invalid request data.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "User does not have permission to manage contributors for this application", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "Application or contributor not found", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    [Authorize(Policy = "CanUpdateApplication")]
    public async Task<IActionResult> RemoveContributorAsync(
        [FromRoute] Guid applicationId,
        [FromRoute] Guid userId,
        CancellationToken cancellationToken)
    {
        var command = new RemoveContributorCommand(applicationId, userId);
        var result = await sender.Send(command, cancellationToken);

        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status200OK
        };
    }

    /// <summary>
    /// Uploads a file for a specific application.
    /// </summary>
    [HttpPost("{applicationId}/files")]
    [Consumes("multipart/form-data")]
    [SwaggerResponse(201, "File uploaded successfully.", typeof(UploadDto))]
    [SwaggerResponse(400, "Invalid request data.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "User does not have permission to manage files for this application", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "Application not found", typeof(ExceptionResponse))]
    [SwaggerResponse(429, "Too Many Requests.", typeof(ExceptionResponse))]
    [SwaggerResponse(409, "Conflict Error.", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    [Authorize(Policy = "CanWriteApplicationFiles")]
    public async Task<IActionResult> UploadFileAsync(
        [FromRoute] Guid applicationId,
        [FromForm] UploadFileRequest dto,
        CancellationToken cancellationToken)
    {
        if (dto.File == null || dto.File.Length == 0)
            throw new BadRequestException("File not provided");

        var command = new UploadFileCommand(
            new ApplicationId(applicationId),
            dto.Name,
            dto.Description,
            dto.File.FileName,
            dto.File.OpenReadStream()
        );
        var result = await sender.Send(command, cancellationToken);

        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status201Created
        };
    }

    /// <summary>
    /// Gets all files for a specific application.
    /// </summary>
    [HttpGet("{applicationId}/files")]
    [SwaggerResponse(200, "List of files for the application.", typeof(IReadOnlyCollection<UploadDto>))]
    [SwaggerResponse(400, "Invalid request data.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "User does not have permission to manage files for this application", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "Application not found", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    [Authorize(Policy = "CanReadApplicationFiles")]
    public async Task<IActionResult> GetFilesForApplicationAsync(
        [FromRoute] Guid applicationId,
        CancellationToken cancellationToken)
    {
        var query = new GetFilesForApplicationQuery(new ApplicationId(applicationId));
        var result = await sender.Send(query, cancellationToken);

        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status200OK
        };
    }

    /// <summary>
    /// Downloads a file by fileId.
    /// </summary>
    [HttpGet("{applicationId}/files/{fileId}/download")]
    [SwaggerResponse(200, "File stream.", typeof(FileStreamResult))]
    [SwaggerResponse(400, "Invalid request data.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "User does not have permission to manage files for this application", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "File not found.", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    [Authorize(Policy = "CanReadApplicationFiles")]
    public async Task<IActionResult> DownloadFileAsync(
        [FromRoute] Guid fileId,
        [FromRoute] Guid applicationId,
        CancellationToken cancellationToken)
    {
        var query = new DownloadFileQuery(fileId, new ApplicationId(applicationId));
        var result = await sender.Send(query, cancellationToken);

        if (result is { IsSuccess: false, Error: not null })
        {
            throw ResultExceptionMapper.ToException(result.ErrorCode, result.Error);
        }

        var file = result.Value!;
        return File(file.FileStream, file.ContentType, file.FileName);
    }

    /// <summary>
    /// Deletes a file by fileId.
    /// </summary>
    [HttpDelete("{applicationId}/files/{fileId}")]
    [SwaggerResponse(200, "File deleted successfully.", typeof(bool))]
    [SwaggerResponse(400, "Invalid request data.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "User does not have permission to manage files for this application", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "File not found.", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    [Authorize(Policy = "CanDeleteApplicationFiles")]
    public async Task<IActionResult> DeleteFileAsync(
        [FromRoute] Guid fileId,
        [FromRoute] Guid applicationId,
        CancellationToken cancellationToken)
    {
        var command = new DeleteFileCommand(fileId, applicationId);
        var result = await sender.Send(command, cancellationToken);

        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status200OK
        };
    }

    /// <summary>
    /// Returns whether file validation currently blocks submit for this application.
    /// </summary>
    [HttpGet("{applicationId}/file-validation-gate")]
    [SwaggerResponse(200, "File validation gate for the application.", typeof(FileValidationGateDto))]
    [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "User does not have permission to read this application", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "Application not found", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    [Authorize(Policy = "CanReadApplication")]
    public async Task<IActionResult> GetFileValidationGateAsync(
        [FromRoute] Guid applicationId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetApplicationFileValidationGateQuery(applicationId), cancellationToken);
        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status200OK
        };
    }

    /// <summary>
    /// Generates and downloads a static HTML preview of the application by reference.
    /// </summary>
    [HttpGet("reference/{applicationReference}/preview/download")]
    [SwaggerResponse(200, "Static HTML file.", typeof(FileStreamResult))]
    [SwaggerResponse(400, "Invalid request data.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "User does not have permission to read this application", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "Application not found", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    [Authorize(Policy = "CanReadAnyApplication")]
    public async Task<IActionResult> DownloadApplicationPreviewHtmlAsync(
        [FromRoute] string applicationReference,
        CancellationToken cancellationToken)
    {
        var query = new GenerateApplicationPreviewHtmlQuery(applicationReference);
        var result = await sender.Send(query, cancellationToken);

        if (result is { IsSuccess: false, Error: not null })
        {
            throw ResultExceptionMapper.ToException(result.ErrorCode, result.Error);
        }

        var file = result.Value!;
        return File(file.FileStream, file.ContentType, file.FileName);
    }
}
