using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.TenantAdmin.Validation;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using System.Text;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin.Commands;

/// <param name="SettingsJson">UTF-8 settings JSON, Base64-encoded.</param>
public sealed record UpsertSafeTenantSettingCommand(
    Guid TenantId,
    string Category,
    string SettingsJson) : IRequest<Result<UpsertTenantSettingResponse>>;

/// <summary>
/// Upserts a non-secret delegated setting (terminology, banner, dashboard) for Tenant Admins.
/// </summary>
public sealed class UpsertSafeTenantSettingCommandHandler(
    ITenantSettingsWriter settingsWriter,
    ITenantContextAccessor tenantContextAccessor,
    IPermissionCheckerService permissionChecker,
    ITenantConfigurationProvider tenantConfigProvider,
    ITenantSettingAuditWriter auditWriter,
    IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<UpsertSafeTenantSettingCommand, Result<UpsertTenantSettingResponse>>
{
    public async Task<Result<UpsertTenantSettingResponse>> Handle(
        UpsertSafeTenantSettingCommand request,
        CancellationToken cancellationToken)
    {
        if (!permissionChecker.IsInteractiveTenantAdmin()
            && !permissionChecker.IsInteractivePlatformAdmin())
        {
            return Result<UpsertTenantSettingResponse>.Forbid(
                "Only interactive Admin users can update organisation settings.");
        }

        var currentTenant = tenantContextAccessor.CurrentTenant;
        if (currentTenant is null)
        {
            return Result<UpsertTenantSettingResponse>.Forbid(
                "Tenant context is required to update organisation settings.");
        }

        if (currentTenant.Id != request.TenantId)
        {
            return Result<UpsertTenantSettingResponse>.Forbid(
                $"Cannot update settings for tenant '{request.TenantId}'. " +
                $"Administrators may only update their own tenant ('{currentTenant.Id}').");
        }

        var category = request.Category?.Trim() ?? string.Empty;
        if (!TenantSafeSettingCategories.IsSafe(category))
        {
            return Result<UpsertTenantSettingResponse>.Failure(
                $"Category '{category}' is not a delegated organisation setting. " +
                $"Allowed: {string.Join(", ", TenantSafeSettingCategories.All)}.");
        }

        string decodedSettingsJson;
        try
        {
            decodedSettingsJson = Encoding.UTF8.GetString(Convert.FromBase64String(request.SettingsJson));
        }
        catch (FormatException)
        {
            return Result<UpsertTenantSettingResponse>.Failure(
                "Invalid Base64 format for SettingsJson");
        }

        if (string.IsNullOrWhiteSpace(decodedSettingsJson))
        {
            return Result<UpsertTenantSettingResponse>.Failure("Settings JSON is required.");
        }

        // EventMappings/SchemaEvents/EventTriggers are read by the API runtime, so they live
        // under the Shared target; everything else stays Web-only.
        var target = TenantSafeSettingCategories.TargetFor(category);

        var validationErrors = TenantSettingJsonValidator.Validate(
            category,
            target,
            decodedSettingsJson);

        if (validationErrors.Count > 0)
        {
            return Result<UpsertTenantSettingResponse>.Validation(
                string.Join(" ", validationErrors));
        }

        try
        {
            var result = await settingsWriter.UpsertSettingAsync(
                request.TenantId,
                category,
                target,
                decodedSettingsJson,
                isSecret: false,
                cancellationToken);

            // A pre-migration copy under the Web target would shadow the Shared row for the Web app.
            if (!string.Equals(target, TenantSafeSettingCategories.DefaultTarget, StringComparison.OrdinalIgnoreCase))
            {
                await settingsWriter.DeleteSettingAsync(
                    request.TenantId,
                    category,
                    TenantSafeSettingCategories.DefaultTarget,
                    cancellationToken);
            }

            await auditWriter.AppendAsync(
                request.TenantId,
                category,
                target,
                result.WasCreated ? "Created" : "Updated",
                ResolveActorEmail(),
                wasSecret: false,
                cancellationToken);

            await tenantConfigProvider.RefreshAsync(cancellationToken);

            var verb = result.WasCreated ? "created" : "updated";
            return Result<UpsertTenantSettingResponse>.Success(
                new UpsertTenantSettingResponse(
                    result.SettingId,
                    result.WasCreated,
                    result.Category,
                    result.Target,
                    $"Organisation setting '{result.Category}' {verb} successfully."));
        }
        catch (KeyNotFoundException ex)
        {
            return Result<UpsertTenantSettingResponse>.NotFound(ex.Message);
        }
    }

    private string ResolveActorEmail()
    {
        var user = httpContextAccessor.HttpContext?.User;
        return user?.FindFirst(ClaimTypes.Email)?.Value
               ?? user?.FindFirst("email")?.Value
               ?? user?.Identity?.Name
               ?? "unknown";
    }
}
