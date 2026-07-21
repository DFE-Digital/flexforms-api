using Asp.Versioning;
using GovUK.Dfe.FlexForms.Application.Users.Commands;
using GovUK.Dfe.FlexForms.Application.Users.Queries;
using GovUK.Dfe.FlexForms.Infrastructure.Security;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
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
public class UsersController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Returns all my permissions.
    /// </summary>
    [HttpGet("/v{version:apiVersion}/me/permissions")]
    [SwaggerResponse(200, "A UserAuthorizationDto object representing the User's Permissions and Roles.", typeof(UserAuthorizationDto))]
    [SwaggerResponse(400, "Invalid request data.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "User not found.", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    [Authorize]
    public async Task<IActionResult> GetMyPermissionsAsync(
        CancellationToken cancellationToken)
    {
        var query = new GetMyPermissionsQuery();
        var result = await sender.Send(query, cancellationToken);

        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status200OK
        };
    }

    /// <summary>
    /// Create and registers a new user using the data in the provided External-IDP token.
    /// </summary>
    [HttpPost("register")]
    [SwaggerResponse(200, "User registered successfully.", typeof(UserDto))]
    [SwaggerResponse(400, "Invalid request data.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    [SwaggerResponse(429, "Too Many Requests.", typeof(ExceptionResponse))]
    [Authorize(Policy = "ServiceCallers")]
    public async Task<ActionResult<UserDto>> RegisterUserAsync(
        [FromBody] RegisterUserRequest request,
        CancellationToken ct)
    {
        var result = await sender.Send(
            new RegisterUserCommand(
                request.AccessToken,
                request.TemplateId == Guid.Empty ? null : request.TemplateId),
            ct);
        
        if (!result.IsSuccess)
            return BadRequest(new ExceptionResponse { Message = result.Error });
        
        return Ok(result.Value);
    }

    /// <summary>
    /// Assigns a predefined role to a user, creating the user when they do not already exist.
    /// </summary>
    [HttpPost("roles")]
    [SwaggerResponse(200, "Role assigned successfully.", typeof(UserDto))]
    [SwaggerResponse(400, "Invalid request data.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Forbidden - only administrators can assign roles", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<UserDto>> AssignUserRoleAsync(
        [FromBody] AssignUserRoleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new AssignUserRoleCommand(request.Email, request.Name, request.Role, request.TemplateIds),
            cancellationToken);

        if (!result.IsSuccess)
        {
            if (result.ErrorCode == DomainErrorCode.Forbidden)
                return StatusCode(StatusCodes.Status403Forbidden, new ExceptionResponse { Message = result.Error });

            return BadRequest(new ExceptionResponse { Message = result.Error });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Lists users who have form access within the current tenant.
    /// </summary>
    [HttpGet("tenant")]
    [SwaggerResponse(200, "Tenant users.", typeof(IReadOnlyCollection<TenantUserDto>))]
    [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Forbidden - only administrators can list tenant users", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IReadOnlyCollection<TenantUserDto>>> GetTenantUsersAsync(
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetTenantUsersQuery(), cancellationToken);

        if (!result.IsSuccess)
        {
            if (result.ErrorCode == DomainErrorCode.Forbidden)
                return StatusCode(StatusCodes.Status403Forbidden, new ExceptionResponse { Message = result.Error });

            return BadRequest(new ExceptionResponse { Message = result.Error });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Replaces a user's form (template) access within the current tenant.
    /// </summary>
    [HttpPut("{userId:guid}/templates")]
    [SwaggerResponse(200, "User template access updated.", typeof(TenantUserDto))]
    [SwaggerResponse(400, "Invalid request data.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Forbidden - only administrators can update template access", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "User not found.", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<TenantUserDto>> UpdateUserTemplateAccessAsync(
        Guid userId,
        [FromBody] UpdateUserTemplateAccessRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new UpdateUserTemplateAccessCommand(userId, request.TemplateIds ?? Array.Empty<Guid>()),
            cancellationToken);

        if (!result.IsSuccess)
        {
            if (result.ErrorCode == DomainErrorCode.Forbidden)
                return StatusCode(StatusCodes.Status403Forbidden, new ExceptionResponse { Message = result.Error });
            if (result.ErrorCode == DomainErrorCode.NotFound)
                return NotFound(new ExceptionResponse { Message = result.Error });

            return BadRequest(new ExceptionResponse { Message = result.Error });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Removes a user from the current tenant by clearing their permissions on tenant templates.
    /// </summary>
    [HttpDelete("{userId:guid}/tenant")]
    [SwaggerResponse(204, "User removed from tenant.")]
    [SwaggerResponse(400, "Invalid request data.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized - no valid user token", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Forbidden - only administrators can remove users from the tenant", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "User not found.", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RemoveUserFromTenantAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RemoveUserFromTenantCommand(userId), cancellationToken);

        if (!result.IsSuccess)
        {
            if (result.ErrorCode == DomainErrorCode.Forbidden)
                return StatusCode(StatusCodes.Status403Forbidden, new ExceptionResponse { Message = result.Error });
            if (result.ErrorCode == DomainErrorCode.NotFound)
                return NotFound(new ExceptionResponse { Message = result.Error });

            return BadRequest(new ExceptionResponse { Message = result.Error });
        }

        return NoContent();
    }
}
