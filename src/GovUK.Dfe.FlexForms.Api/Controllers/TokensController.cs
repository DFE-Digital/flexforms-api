using Asp.Versioning;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Users.Commands;
using GovUK.Dfe.FlexForms.Application.Users.Queries;
using GovUK.Dfe.FlexForms.Infrastructure.Security;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.FlexForms.Application.Applications.Commands;
using System.Threading;

namespace GovUK.Dfe.FlexForms.Api.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("v{version:apiVersion}/[controller]")]
    public class TokensController(ISender sender) : ControllerBase
    {
        /// <summary>
        /// Exchanges an DSI token for our ExternalApplications InternalUser JWT.
        /// </summary>
        [HttpPost("exchange")]
        [SwaggerResponse(200, "Token exchanged successfully.", typeof(ExchangeTokenDto))]
        [SwaggerResponse(400, "Invalid request data.", typeof(ExceptionResponse))]
        [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
        [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
        [Authorize(Policy = "ServiceCallers")]
        public async Task<ActionResult<ExchangeTokenDto>> Exchange(
            [FromBody] ExchangeTokenRequest request,
            CancellationToken cancellationToken)
        {
            var result = await sender.Send(
            new ExchangeTokenQuery(request.AccessToken), cancellationToken);

            return new ObjectResult(result)
            {
                StatusCode = StatusCodes.Status200OK
            };
        }

        /// <summary>
        /// Generates a 6 digit Test Authentication one-time password and emails it to the user.
        /// The password is valid for one hour.
        /// </summary>
        [HttpPost("test-auth-password")]
        [SwaggerResponse(202, "One-time password emailed to the user.")]
        [SwaggerResponse(400, "Invalid request data.", typeof(ExceptionResponse))]
        [SwaggerResponse(401, "Unauthorized - no valid service token", typeof(ExceptionResponse))]
        [SwaggerResponse(403, "Test Authentication is not enabled for this tenant.", typeof(ExceptionResponse))]
        [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
        [Authorize(Policy = "ServiceCallers")]
        public async Task<IActionResult> SendTestAuthPassword(
            [FromBody] SendTestAuthPasswordRequest request,
            CancellationToken cancellationToken)
        {
            var result = await sender.Send(new SendTestAuthPasswordCommand(request.Email), cancellationToken);

            if (!result.IsSuccess)
                return ToErrorResult(result.ErrorCode, result.Error);

            return Accepted();
        }

        /// <summary>
        /// Verifies a Test Authentication one-time password. A valid password is consumed.
        /// </summary>
        [HttpPost("test-auth-password/verify")]
        [SwaggerResponse(200, "Verification outcome.", typeof(VerifyTestAuthPasswordResponse))]
        [SwaggerResponse(400, "Invalid request data.", typeof(ExceptionResponse))]
        [SwaggerResponse(401, "Unauthorized - no valid service token", typeof(ExceptionResponse))]
        [SwaggerResponse(403, "Test Authentication is not enabled for this tenant.", typeof(ExceptionResponse))]
        [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
        [Authorize(Policy = "ServiceCallers")]
        public async Task<ActionResult<VerifyTestAuthPasswordResponse>> VerifyTestAuthPassword(
            [FromBody] VerifyTestAuthPasswordRequest request,
            CancellationToken cancellationToken)
        {
            var result = await sender.Send(
                new VerifyTestAuthPasswordCommand(request.Email, request.Password),
                cancellationToken);

            if (!result.IsSuccess)
                return ToErrorResult(result.ErrorCode, result.Error);

            return Ok(result.Value);
        }

        private ObjectResult ToErrorResult(DomainErrorCode? errorCode, string? error)
        {
            if (errorCode == DomainErrorCode.Forbidden)
                return StatusCode(StatusCodes.Status403Forbidden, new ExceptionResponse { Message = error });

            return BadRequest(new ExceptionResponse { Message = error });
        }
    }
}
