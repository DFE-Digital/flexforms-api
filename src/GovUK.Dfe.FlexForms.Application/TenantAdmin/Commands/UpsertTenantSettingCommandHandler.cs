using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Application.TenantAdmin.Validation;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using MediatR;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin.Commands;

/// <param name="SettingsJson">
/// UTF-8 settings JSON, Base64-encoded (WAF-safe transport; decoded before persistence).
/// </param>
public sealed record UpsertTenantSettingCommand(
    Guid TenantId,
    string Category,
    string Target,
    string SettingsJson,
    bool IsSecret) : IRequest<Result<UpsertTenantSettingResponse>>;

/// <summary>
/// Upserts a TenantConfig settings category for a tenant.
/// Callers must be interactive tenant Admin or SuperAdmin and may only mutate the tenant resolved for the current request.
/// </summary>
public sealed class UpsertTenantSettingCommandHandler(
    ITenantSettingsWriter settingsWriter,
    ITenantContextAccessor tenantContextAccessor,
    IPermissionCheckerService permissionChecker,
    ITenantConfigurationProvider tenantConfigProvider,
    ITenantSettingAuditWriter auditWriter,
    IHttpContextAccessor httpContextAccessor,
    IHostEnvironment hostEnvironment)
    : IRequestHandler<UpsertTenantSettingCommand, Result<UpsertTenantSettingResponse>>
{
    public async Task<Result<UpsertTenantSettingResponse>> Handle(
        UpsertTenantSettingCommand request,
        CancellationToken cancellationToken)
    {
        if (!permissionChecker.IsInteractiveTenantAdmin())
        {
            return Result<UpsertTenantSettingResponse>.Forbid(
                "Only interactive tenant administrators can update tenant settings. Client-credentials / service tokens are not allowed.");
        }

        var currentTenant = tenantContextAccessor.CurrentTenant;
        if (currentTenant is null)
        {
            return Result<UpsertTenantSettingResponse>.Forbid(
                "Tenant context is required to update tenant settings.");
        }

        if (currentTenant.Id != request.TenantId)
        {
            return Result<UpsertTenantSettingResponse>.Forbid(
                $"Cannot update settings for tenant '{request.TenantId}'. " +
                $"Administrators may only update their own tenant ('{currentTenant.Id}').");
        }

        string decodedSettingsJson;
        try
        {
            var bytes = Convert.FromBase64String(request.SettingsJson);
            decodedSettingsJson = Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException)
        {
            return Result<UpsertTenantSettingResponse>.Failure(
                "Invalid Base64 format for SettingsJson");
        }

        if (string.IsNullOrWhiteSpace(decodedSettingsJson))
        {
            return Result<UpsertTenantSettingResponse>.Failure(
                "Settings JSON is required.");
        }

        var validationErrors = TenantSettingJsonValidator.Validate(
            request.Category,
            request.Target,
            decodedSettingsJson);

        if (validationErrors.Count > 0)
        {
            return Result<UpsertTenantSettingResponse>.Validation(
                string.Join(" ", validationErrors));
        }

        if (TestAuthenticationEnvironmentGate.IsProduction(hostEnvironment)
            && WouldEnableTestAuthentication(request.Category, decodedSettingsJson))
        {
            return Result<UpsertTenantSettingResponse>.Validation(
                "Test Authentication cannot be enabled or selected in Production.");
        }

        var effectiveIsSecret = request.IsSecret
            || TenantSettingsSecretCategories.ShouldEncrypt(request.Category);

        try
        {
            var result = await settingsWriter.UpsertSettingAsync(
                request.TenantId,
                request.Category,
                request.Target,
                decodedSettingsJson,
                effectiveIsSecret,
                cancellationToken);

            await auditWriter.AppendAsync(
                request.TenantId,
                request.Category,
                request.Target,
                result.WasCreated ? "Created" : "Updated",
                ResolveActorEmail(),
                effectiveIsSecret,
                cancellationToken);

            await tenantConfigProvider.RefreshAsync(cancellationToken);

            var verb = result.WasCreated ? "created" : "updated";

            return Result<UpsertTenantSettingResponse>.Success(
                new UpsertTenantSettingResponse(
                    result.SettingId,
                    result.WasCreated,
                    result.Category,
                    result.Target,
                    $"Setting '{result.Category}' (Target={result.Target}) {verb} successfully."));
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

    private static bool WouldEnableTestAuthentication(string category, string settingsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (string.Equals(category, "TestAuthentication", StringComparison.OrdinalIgnoreCase))
            {
                return root.TryGetProperty("Enabled", out var enabled)
                       && enabled.ValueKind is JsonValueKind.True
                           or JsonValueKind.String
                       && (enabled.ValueKind == JsonValueKind.True
                           || (enabled.ValueKind == JsonValueKind.String
                               && bool.TryParse(enabled.GetString(), out var parsed)
                               && parsed));
            }

            if (string.Equals(category, "Authentication", StringComparison.OrdinalIgnoreCase)
                || string.Equals(category, "InteractiveAuthentication", StringComparison.OrdinalIgnoreCase))
            {
                if (!root.TryGetProperty("Scheme", out var scheme)
                    || scheme.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                var value = scheme.GetString()?.Trim();
                return string.Equals(value, "TestAuthentication", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(value, "Test", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(value, "TestAuth", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }
}
