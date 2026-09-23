using System.Text;
using System.Text.Json;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Application.TenantAdmin.Validation;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;
using Microsoft.Extensions.Hosting;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin.Commands;

public sealed record ValidateTenantSettingCommand(
    Guid TenantId,
    string Category,
    string Target,
    string SettingsJson,
    bool IsSecret) : IRequest<Result<ValidateTenantSettingResponse>>;

public sealed class ValidateTenantSettingCommandHandler(
    ITenantContextAccessor tenantContextAccessor,
    IPermissionCheckerService permissionChecker,
    ITenantSettingsQuery settingsQuery,
    ITemplateHostMappingOwnershipValidator templateMappingOwnershipValidator,
    IHostEnvironment hostEnvironment)
    : IRequestHandler<ValidateTenantSettingCommand, Result<ValidateTenantSettingResponse>>
{
    public async Task<Result<ValidateTenantSettingResponse>> Handle(
        ValidateTenantSettingCommand request,
        CancellationToken cancellationToken)
    {
        if (!permissionChecker.IsInteractiveTenantAdmin())
        {
            return Result<ValidateTenantSettingResponse>.Forbid(
                "Only interactive tenant administrators can validate tenant settings.");
        }

        var currentTenant = tenantContextAccessor.CurrentTenant;
        if (currentTenant is null || currentTenant.Id != request.TenantId)
        {
            return Result<ValidateTenantSettingResponse>.Forbid(
                "Administrators may only validate settings for their own tenant.");
        }

        if (SuperAdminOnlyTenantSettingCategories.IsRestricted(request.Category)
            && !permissionChecker.IsInteractivePlatformAdmin())
        {
            return Result<ValidateTenantSettingResponse>.Forbid(
                $"Only SuperAdmin can validate '{request.Category}' settings.");
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(request.SettingsJson));
        }
        catch (FormatException)
        {
            return Result<ValidateTenantSettingResponse>.Validation(
                "Invalid Base64 format for SettingsJson");
        }

        var category = request.Category?.Trim() ?? string.Empty;
        var target = request.Target?.Trim() ?? string.Empty;

        var list = await settingsQuery.ListSettingsAsync(request.TenantId, cancellationToken);
        var existing = list?.Settings.FirstOrDefault(s =>
            string.Equals(s.Category, category, StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.Target, target, StringComparison.OrdinalIgnoreCase));

        if (existing is { IsSecret: true }
            && !request.IsSecret
            && !TenantSettingsSecretCategories.ShouldEncrypt(category)
            && !permissionChecker.IsInteractivePlatformAdmin())
        {
            return Result<ValidateTenantSettingResponse>.Forbid(
                "Only SuperAdmin can remove the Secret flag from a setting that is already marked secret.");
        }

        var errors = new List<string>();
        if (existing is { IsSecret: true })
        {
            var restored = TenantSettingSecretJson.Restore(decoded, existing.SettingsJson);
            if (restored.Errors.Count > 0)
            {
                errors.AddRange(restored.Errors);
            }
            else
            {
                decoded = restored.Json;
            }
        }

        if (errors.Count == 0)
        {
            errors.AddRange(TenantSettingJsonValidator.Validate(
                category, target, decoded, TenantSettingValidationMode.Strict));
        }

        if (errors.Count == 0)
        {
            var ownershipErrors = await templateMappingOwnershipValidator.ValidateAsync(
                request.TenantId,
                category,
                decoded,
                cancellationToken);
            errors.AddRange(ownershipErrors);
        }

        var includePlaintext = TenantSettingSecretPlaintextGate.AllowsSuperAdminPlaintext(
            hostEnvironment,
            permissionChecker.IsInteractivePlatformAdmin());

        var currentJson = existing is null
            ? null
            : existing.IsSecret && !includePlaintext
                ? TenantSettingSecretJson.Redact(existing.SettingsJson, existing.Category).Json
                : existing.SettingsJson;
        var proposedJson = (existing is { IsSecret: true } || request.IsSecret) && !includePlaintext
            ? TenantSettingSecretJson.Redact(decoded, category).Json
            : decoded;
        var diffSummary = BuildDiffSummary(
            existing?.SettingsJson,
            decoded,
            existing is not null);

        return Result<ValidateTenantSettingResponse>.Success(
            new ValidateTenantSettingResponse(
                errors.Count == 0,
                errors,
                diffSummary,
                currentJson,
                proposedJson,
                existing is not null));
    }

    private static string BuildDiffSummary(string? currentJson, string proposedJson, bool exists)
    {
        if (!exists)
        {
            return "New setting — will be created.";
        }

        var currentNormalized = NormalizeJson(currentJson);
        var proposedNormalized = NormalizeJson(proposedJson);

        if (string.Equals(currentNormalized, proposedNormalized, StringComparison.Ordinal))
        {
            return "No JSON content changes detected.";
        }

        var currentLen = currentJson?.Length ?? 0;
        var proposedLen = proposedJson.Length;
        return $"JSON will change ({currentLen} → {proposedLen} characters). Review Current vs Proposed below.";
    }

    private static string NormalizeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement);
        }
        catch (JsonException)
        {
            return json.Trim();
        }
    }
}
