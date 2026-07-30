using Asp.Versioning;
using GovUK.Dfe.FlexForms.Application.HostConfig.Queries;
using GovUK.Dfe.FlexForms.Infrastructure.Security;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Http.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace GovUK.Dfe.FlexForms.Api.Controllers;

/// <summary>
/// Platform endpoint that exposes global host configuration for downstream applications
/// (e.g. Web startup bootstrap). Does not require tenant context.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/host-config")]
[Authorize(Policy = PlatformConstants.PlatformHostPolicy)]
public class HostConfigController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Returns global host configuration safe for the consuming target (e.g. logging, App Insights, Redis).
    /// </summary>
    /// <param name="target">The consuming application's target. One of: Web, Api. Defaults to Web.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet]
    [SwaggerResponse(200, "Host configuration.", typeof(HostConfigurationDto))]
    [SwaggerResponse(400, "Invalid target.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized.", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Missing Platform.Host.Read app role.", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    public async Task<IActionResult> GetHostConfiguration(
        [FromQuery] string target = "Web",
        CancellationToken cancellationToken = default)
    {
        
        var result = await sender.Send(new GetHostConfigurationQuery(target), cancellationToken);

        if (!result.IsSuccess)
        {
            var statusCode = result.ErrorCode switch
            {
                DomainErrorCode.Validation => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status400BadRequest
            };

            return new ObjectResult(result) { StatusCode = statusCode };
        }

        return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
    }
}
