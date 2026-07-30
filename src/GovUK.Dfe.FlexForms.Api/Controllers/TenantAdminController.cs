using Asp.Versioning;
using GovUK.Dfe.FlexForms.Application.TenantAdmin;
using GovUK.Dfe.FlexForms.Application.TenantAdmin.Commands;
using GovUK.Dfe.FlexForms.Application.TenantAdmin.Queries;
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

/// <summary>
/// Administrative endpoints for tenant configuration management.
/// Tenant-facing actions require an interactive Admin <strong>user</strong> JWT
/// (not Entra client-credentials / machine tokens). Admins may only manage their
/// resolved tenant (from <c>X-Tenant-ID</c> / Origin). Platform-wide seed uses the
/// platform TenantConfig app role.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/admin/tenants")]
public class TenantAdminController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Triggers an immediate refresh of the in-memory tenant configuration cache.
    /// Requires an interactive Admin user JWT.
    /// </summary>
    [HttpPost("refresh")]
    [Authorize(Policy = AuthConstants.TenantAdminUserPolicy)]
    [SwaggerResponse(200, "Tenant configuration refreshed.", typeof(RefreshTenantConfigurationResponse))]
    [SwaggerResponse(401, "Unauthorized.", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Forbidden - interactive Admin user required.", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    public async Task<IActionResult> RefreshTenantConfiguration(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RefreshTenantConfigurationCommand(), cancellationToken);

        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status200OK
        };
    }

    /// <summary>
    /// Returns a summary of the caller's own tenant (not the full SaaS catalogue).
    /// Requires an interactive Admin user JWT.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = AuthConstants.TenantAdminUserPolicy)]
    [SwaggerResponse(200, "List of tenants.", typeof(GetTenantsResponse))]
    [SwaggerResponse(401, "Unauthorized.", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Forbidden - interactive Admin user required.", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    public async Task<IActionResult> GetTenants(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetTenantsQuery(), cancellationToken);

        if (!result.IsSuccess)
        {
            return MapFailure(result);
        }

        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status200OK
        };
    }

    /// <summary>
    /// Seeds tenant configuration from appsettings into the tenant config database.
    /// Platform-only: requires <c>Platform.TenantConfig.Read</c> (machine / platform app role).
    /// </summary>
    [HttpPost("seed")]
    [Authorize(Policy = PlatformConstants.PlatformTenantConfigPolicy)]
    [SwaggerResponse(200, "Seeding complete.", typeof(SeedTenantsResponse))]
    [SwaggerResponse(401, "Unauthorized.", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Forbidden - Missing Platform.TenantConfig.Read app role.", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    public async Task<IActionResult> SeedFromAppSettings(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new SeedTenantsFromAppSettingsCommand(), cancellationToken);

        return new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status200OK
        };
    }

    /// <summary>
    /// Returns decrypted TenantConfig settings rows for the caller's own tenant.
    /// Restricted to interactive SuperAdmin users.
    /// </summary>
    [HttpGet("{tenantId:guid}/settings")]
    [Authorize(Policy = AuthConstants.TenantAdminUserPolicy)]
    [SwaggerResponse(200, "Tenant settings.", typeof(GetTenantSettingsResponse))]
    [SwaggerResponse(401, "Unauthorized.", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Forbidden - interactive SuperAdmin of own tenant required.", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "Tenant not found.", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    public async Task<IActionResult> GetTenantSettings(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetTenantSettingsQuery(tenantId), cancellationToken);

        if (!result.IsSuccess)
            return MapFailure(result);

        return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
    }

    /// <summary>
    /// Adds or updates a configuration section for the caller's own tenant only.
    /// Requires an interactive SuperAdmin user JWT; the route <paramref name="tenantId"/> must
    /// match the resolved tenant context.
    /// Uses POST and Base64-encoded SettingsJson (same WAF-safe pattern as template schemas).
    /// </summary>
    [HttpPost("{tenantId:guid}/settings")]
    [Authorize(Policy = AuthConstants.TenantAdminUserPolicy)]
    [SwaggerResponse(200, "Setting updated.", typeof(UpsertTenantSettingResponse))]
    [SwaggerResponse(201, "Setting created.", typeof(UpsertTenantSettingResponse))]
    [SwaggerResponse(400, "Validation error.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized.", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Forbidden - interactive SuperAdmin of own tenant required.", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "Tenant not found.", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    public async Task<IActionResult> UpsertTenantSetting(
        Guid tenantId,
        [FromBody] UpsertTenantSettingRequest body,
        CancellationToken cancellationToken)
    {
        var command = new UpsertTenantSettingCommand(
            tenantId,
            body.Category,
            body.Target,
            body.SettingsJson,
            body.IsSecret);

        var result = await sender.Send(command, cancellationToken);

        if (!result.IsSuccess)
            return MapFailure(result);

        var statusCode = result.Value!.WasCreated
            ? StatusCodes.Status201Created
            : StatusCodes.Status200OK;

        return new ObjectResult(result) { StatusCode = statusCode };
    }

    private static IActionResult MapFailure<T>(Result<T> result)
    {
        var statusCode = result.ErrorCode switch
        {
            DomainErrorCode.Forbidden => StatusCodes.Status403Forbidden,
            DomainErrorCode.NotFound => StatusCodes.Status404NotFound,
            DomainErrorCode.Validation => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status400BadRequest
        };

        // Return ExceptionResponse directly so API clients can deserialize 4xx bodies
        // (and so we do not rely solely on ResultToExceptionFilter).
        return new ObjectResult(new ExceptionResponse
        {
            StatusCode = statusCode,
            Message = result.Error ?? "Request failed",
            ExceptionType = result.ErrorCode?.ToString() ?? "Error"
        })
        {
            StatusCode = statusCode
        };
    }
}
