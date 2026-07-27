using Asp.Versioning;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.FlexForms.Application.Roles.Commands;
using GovUK.Dfe.FlexForms.Application.Roles.Queries;
using GovUK.Dfe.FlexForms.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace GovUK.Dfe.FlexForms.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/[controller]")]
public class RolesController(ISender sender) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = $"{RoleNames.SuperAdmin},{RoleNames.Admin}")]
    [SwaggerResponse(200, "Tenant roles.", typeof(IReadOnlyCollection<TenantRoleDto>))]
    [SwaggerResponse(403, "Forbidden.", typeof(ExceptionResponse))]
    public async Task<ActionResult<IReadOnlyCollection<TenantRoleDto>>> ListAsync(
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ListTenantRolesQuery(), cancellationToken);
        return Map(result);
    }

    [HttpPost]
    [Authorize(Roles = $"{RoleNames.SuperAdmin},{RoleNames.Admin}")]
    [SwaggerResponse(200, "Role created.", typeof(TenantRoleDto))]
    [SwaggerResponse(400, "Invalid request.", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Forbidden.", typeof(ExceptionResponse))]
    public async Task<ActionResult<TenantRoleDto>> CreateAsync(
        [FromBody] CreateTenantRoleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateTenantRoleCommand(request.Name), cancellationToken);
        return Map(result);
    }

    [HttpPut("{roleId:guid}")]
    [Authorize(Roles = $"{RoleNames.SuperAdmin},{RoleNames.Admin}")]
    [SwaggerResponse(200, "Role renamed.", typeof(TenantRoleDto))]
    [SwaggerResponse(404, "Not found.", typeof(ExceptionResponse))]
    public async Task<ActionResult<TenantRoleDto>> RenameAsync(
        Guid roleId,
        [FromBody] RenameTenantRoleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RenameTenantRoleCommand(roleId, request.Name), cancellationToken);
        return Map(result);
    }

    [HttpDelete("{roleId:guid}")]
    [Authorize(Roles = $"{RoleNames.SuperAdmin},{RoleNames.Admin}")]
    [SwaggerResponse(200, "Role deleted.")]
    [SwaggerResponse(404, "Not found.", typeof(ExceptionResponse))]
    public async Task<IActionResult> DeleteAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteTenantRoleCommand(roleId), cancellationToken);
        if (!result.IsSuccess)
            return MapFailure(result);
        return Ok();
    }

    [HttpGet("{roleId:guid}/permissions")]
    [Authorize(Roles = $"{RoleNames.SuperAdmin},{RoleNames.Admin}")]
    [SwaggerResponse(200, "Role permissions.", typeof(IReadOnlyCollection<RolePermissionDto>))]
    public async Task<ActionResult<IReadOnlyCollection<RolePermissionDto>>> GetPermissionsAsync(
        Guid roleId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetRolePermissionsQuery(roleId), cancellationToken);
        return Map(result);
    }

    [HttpPut("{roleId:guid}/permissions")]
    [Authorize(Roles = $"{RoleNames.SuperAdmin},{RoleNames.Admin}")]
    [SwaggerResponse(200, "Permissions replaced.", typeof(IReadOnlyCollection<RolePermissionDto>))]
    public async Task<ActionResult<IReadOnlyCollection<RolePermissionDto>>> SetPermissionsAsync(
        Guid roleId,
        [FromBody] SetRolePermissionsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new SetRolePermissionsCommand(roleId, request.Permissions),
            cancellationToken);
        return Map(result);
    }

    private ActionResult<T> Map<T>(Result<T> result)
    {
        if (result.IsSuccess)
            return Ok(result.Value);

        return MapFailure(result);
    }

    private ActionResult MapFailure<T>(Result<T> result)
    {
        var body = new ExceptionResponse { Message = result.Error };
        return result.ErrorCode switch
        {
            DomainErrorCode.Forbidden => StatusCode(StatusCodes.Status403Forbidden, body),
            DomainErrorCode.NotFound => NotFound(body),
            _ => BadRequest(body)
        };
    }
}
