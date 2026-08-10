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
    /// Returns non-secret organisation settings (terminology, banner, dashboard) for Tenant Admins.
    /// </summary>
    [HttpGet("{tenantId:guid}/safe-settings")]
    [Authorize(Policy = AuthConstants.TenantAdminUserPolicy)]
    [SwaggerResponse(200, "Organisation settings.", typeof(GetTenantSettingsResponse))]
    public async Task<IActionResult> GetSafeTenantSettings(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetSafeTenantSettingsQuery(tenantId), cancellationToken);
        if (!result.IsSuccess)
            return MapFailure(result);

        return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
    }

    /// <summary>
    /// Upserts a delegated non-secret organisation setting (Tenant Admin or SuperAdmin).
    /// </summary>
    [HttpPut("{tenantId:guid}/safe-settings")]
    [Authorize(Policy = AuthConstants.TenantAdminUserPolicy)]
    [SwaggerResponse(200, "Setting upserted.", typeof(UpsertTenantSettingResponse))]
    public async Task<IActionResult> UpsertSafeTenantSetting(
        Guid tenantId,
        [FromBody] UpsertTenantSettingRequest body,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new UpsertSafeTenantSettingCommand(tenantId, body.Category, body.SettingsJson),
            cancellationToken);

        if (!result.IsSuccess)
            return MapFailure(result);

        return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
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
    /// Clones the caller's own tenant into a new TenantConfig tenant.
    /// Copies all settings (re-encrypting secrets). Requires a unique name, hostname and origin.
    /// Principals are not copied. Interactive SuperAdmin only.
    /// Secrets are sent only inside Base64 <c>payloadJson</c> (WAF-safe; avoids secret property names on the wire).
    /// </summary>
    [HttpPost("{tenantId:guid}/clone")]
    [Authorize(Policy = AuthConstants.TenantAdminUserPolicy)]
    [SwaggerResponse(201, "Tenant cloned.", typeof(DuplicateTenantResponse))]
    [SwaggerResponse(400, "Validation error.", typeof(ExceptionResponse))]
    [SwaggerResponse(401, "Unauthorized.", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Forbidden - interactive SuperAdmin of own tenant required.", typeof(ExceptionResponse))]
    [SwaggerResponse(404, "Source tenant not found.", typeof(ExceptionResponse))]
    [SwaggerResponse(500, "Internal server error.", typeof(ExceptionResponse))]
    public async Task<IActionResult> CloneTenant(
        Guid tenantId,
        [FromBody] CloneTenantRequest body,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new DuplicateTenantCommand(
                tenantId,
                body.NewTenantId,
                body.NewTenantName,
                body.Hostname,
                body.FrontendOrigin,
                body.PayloadJson),
            cancellationToken);

        if (!result.IsSuccess)
            return MapFailure(result);

        return new ObjectResult(result) { StatusCode = StatusCodes.Status201Created };
    }

    /// <summary>
    /// Legacy alias for <see cref="CloneTenant"/>. Prefer <c>POST .../clone</c>.
    /// </summary>
    [HttpPost("{tenantId:guid}/duplicate")]
    [Authorize(Policy = AuthConstants.TenantAdminUserPolicy)]
    [ApiExplorerSettings(IgnoreApi = true)]
    public Task<IActionResult> DuplicateTenant(
        Guid tenantId,
        [FromBody] CloneTenantRequest body,
        CancellationToken cancellationToken)
        => CloneTenant(tenantId, body, cancellationToken);

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

    /// <summary>
    /// Returns the effective runtime configuration for the caller's tenant (auth scheme, hostnames, cache metadata).
    /// </summary>
    [HttpGet("{tenantId:guid}/effective-config")]
    [Authorize(Policy = AuthConstants.TenantAdminUserPolicy)]
    [SwaggerResponse(200, "Effective tenant configuration.", typeof(TenantEffectiveConfigurationDto))]
    public async Task<IActionResult> GetEffectiveConfiguration(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetTenantEffectiveConfigurationQuery(tenantId), cancellationToken);
        if (!result.IsSuccess)
            return MapFailure(result);

        return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
    }

    /// <summary>
    /// Exports tenant settings for promotion to another environment (secrets redacted).
    /// </summary>
    [HttpGet("{tenantId:guid}/export")]
    [Authorize(Policy = AuthConstants.TenantAdminUserPolicy)]
    [SwaggerResponse(200, "Export bundle.", typeof(ExportTenantConfigurationDto))]
    public async Task<IActionResult> ExportConfiguration(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ExportTenantConfigurationQuery(tenantId), cancellationToken);
        if (!result.IsSuccess)
            return MapFailure(result);

        return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
    }

    /// <summary>
    /// Imports a promotion bundle into the caller's tenant.
    /// </summary>
    [HttpPost("{tenantId:guid}/import")]
    [Authorize(Policy = AuthConstants.TenantAdminUserPolicy)]
    [SwaggerResponse(200, "Import result.", typeof(ImportTenantConfigurationResultDto))]
    public async Task<IActionResult> ImportConfiguration(
        Guid tenantId,
        [FromBody] ImportTenantConfigurationDto body,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ImportTenantConfigurationCommand(tenantId, body), cancellationToken);
        if (!result.IsSuccess)
            return MapFailure(result);

        return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
    }

    /// <summary>
    /// Returns recent tenant setting change audit entries.
    /// </summary>
    [HttpGet("{tenantId:guid}/settings/audit")]
    [Authorize(Policy = AuthConstants.TenantAdminUserPolicy)]
    [SwaggerResponse(200, "Audit log.", typeof(GetTenantSettingAuditLogDto))]
    public async Task<IActionResult> GetSettingAuditLog(
        Guid tenantId,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new GetTenantSettingAuditLogQuery(tenantId, take), cancellationToken);
        if (!result.IsSuccess)
            return MapFailure(result);

        return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
    }

    /// <summary>
    /// Dry-run validation and diff for a proposed tenant setting change.
    /// </summary>
    [HttpPost("{tenantId:guid}/settings/validate")]
    [Authorize(Policy = AuthConstants.TenantAdminUserPolicy)]
    [SwaggerResponse(200, "Validation result.", typeof(ValidateTenantSettingResponse))]
    public async Task<IActionResult> ValidateTenantSetting(
        Guid tenantId,
        [FromBody] ValidateTenantSettingRequest body,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new ValidateTenantSettingCommand(
                tenantId,
                body.Category,
                body.Target,
                body.SettingsJson,
                body.IsSecret),
            cancellationToken);

        if (!result.IsSuccess)
            return MapFailure(result);

        return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
    }

    /// <summary>
    /// Deletes a tenant setting category. Interactive SuperAdmin only.
    /// </summary>
    [HttpDelete("{tenantId:guid}/settings")]
    [Authorize(Policy = AuthConstants.TenantAdminUserPolicy)]
    [SwaggerResponse(200, "Setting deleted.", typeof(DeleteTenantSettingResponse))]
    public async Task<IActionResult> DeleteTenantSetting(
        Guid tenantId,
        [FromQuery] string category,
        [FromQuery] string target,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new DeleteTenantSettingCommand(tenantId, category, target),
            cancellationToken);

        if (!result.IsSuccess)
            return MapFailure(result);

        return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
    }

    /// <summary>
    /// Tenant health checks for SuperAdmin Tenant Settings.
    /// </summary>
    [HttpGet("{tenantId:guid}/health")]
    [Authorize(Policy = AuthConstants.TenantAdminUserPolicy)]
    [SwaggerResponse(200, "Tenant health.", typeof(TenantHealthDto))]
    public async Task<IActionResult> GetTenantHealth(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetTenantHealthQuery(tenantId), cancellationToken);
        if (!result.IsSuccess)
            return MapFailure(result);

        return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
    }

    /// <summary>
    /// Category cookbook (examples and notes) for SuperAdmin UI.
    /// </summary>
    [HttpGet("settings/cookbook")]
    [Authorize(Policy = AuthConstants.TenantAdminUserPolicy)]
    [SwaggerResponse(200, "Category cookbook.", typeof(GetTenantSettingCategoryCookbookResponse))]
    public async Task<IActionResult> GetCategoryCookbook(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetTenantSettingCategoryCookbookQuery(), cancellationToken);
        if (!result.IsSuccess)
            return MapFailure(result);

        return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
    }

    /// <summary>
    /// Platform typed-event catalogue (CoreLibs Messaging.Contracts) with property schema.
    /// </summary>
    [HttpGet("event-catalogue")]
    [Authorize(Policy = AuthConstants.TenantAdminUserPolicy)]
    [SwaggerResponse(200, "Event catalogue.", typeof(GetEventCatalogueResponse))]
    [SwaggerResponse(401, "Unauthorized.", typeof(ExceptionResponse))]
    [SwaggerResponse(403, "Forbidden - interactive Admin user required.", typeof(ExceptionResponse))]
    public async Task<IActionResult> GetEventCatalogue(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetEventCatalogueQuery(), cancellationToken);
        if (!result.IsSuccess)
            return MapFailure(result);

        return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
    }

    /// <summary>
    /// Read-only platform tenant catalogue (SuperAdmin only).
    /// </summary>
    [HttpGet("platform")]
    [Authorize(Policy = AuthConstants.TenantAdminUserPolicy)]
    [SwaggerResponse(200, "Platform tenants.", typeof(GetPlatformTenantsResponse))]
    public async Task<IActionResult> GetPlatformTenants(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetPlatformTenantsQuery(), cancellationToken);
        if (!result.IsSuccess)
            return MapFailure(result);

        return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
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
