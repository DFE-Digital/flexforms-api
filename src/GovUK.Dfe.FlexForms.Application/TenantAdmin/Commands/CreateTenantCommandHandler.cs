using System.Text.Json;
using FluentValidation;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Common;
using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Application.TenantAdmin.Validation;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;
using Microsoft.Extensions.Hosting;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin.Commands;

/// <param name="SourceTenantId">
/// The caller's own tenant. Only used to restore <c>__REDACTED__</c> secret leaves from its
/// matching Category + Target row; nothing else is copied from it.
/// </param>
/// <param name="PayloadJson">
/// Base64-encoded UTF-8 JSON matching <see cref="NewTenantPayload"/> (WAF-safe transport).
/// </param>
public sealed record CreateTenantCommand(
    Guid SourceTenantId,
    Guid NewTenantId,
    string NewTenantName,
    string PayloadJson)
    : IRequest<Result<DuplicateTenantResponse>>;

internal sealed class CreateTenantCommandValidator : AbstractValidator<CreateTenantCommand>
{
    public CreateTenantCommandValidator()
    {
        RuleFor(x => x.SourceTenantId).NotEmpty();
        RuleFor(x => x.NewTenantId).NotEmpty();
        RuleFor(x => x.NewTenantName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PayloadJson)
            .NotEmpty()
            .Must(WafSafeUtf8Base64.IsValidBase64)
            .WithMessage("PayloadJson must be a valid Base64-encoded UTF-8 string.");
    }
}

/// <summary>
/// Decoded <see cref="CreateTenantCommand.PayloadJson"/>.
/// </summary>
public sealed class NewTenantPayload
{
    public string Hostname { get; set; } = string.Empty;

    public string FrontendOrigin { get; set; } = string.Empty;

    public List<NewTenantPayloadSetting> Settings { get; set; } = [];
}

public sealed class NewTenantPayloadSetting
{
    public string Category { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;

    public string SettingsJson { get; set; } = string.Empty;
}

/// <summary>
/// Creates a new TenantConfig tenant from the settings the SuperAdmin reviewed on the New tenant page.
/// Only <see cref="NewTenantSettingsRequirements.Categories"/> (required + optional) are accepted, and
/// the result must satisfy <see cref="NewTenantSettingsRequirements.Validate"/>. Interactive SuperAdmin only.
/// </summary>
public sealed class CreateTenantCommandHandler(
    ITenantCreator tenantCreator,
    ITenantSettingsQuery settingsQuery,
    ITenantContextAccessor tenantContextAccessor,
    IPermissionCheckerService permissionChecker,
    ITenantConfigurationProvider tenantConfigProvider,
    ITemplateHostMappingOwnershipValidator templateMappingOwnershipValidator,
    IHostEnvironment hostEnvironment)
    : IRequestHandler<CreateTenantCommand, Result<DuplicateTenantResponse>>
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] Targets = [TenantSettingTargetOverlap.SharedTarget, "Api", "Web"];

    public async Task<Result<DuplicateTenantResponse>> Handle(
        CreateTenantCommand request,
        CancellationToken cancellationToken)
    {
        if (!permissionChecker.IsInteractivePlatformAdmin())
        {
            return Result<DuplicateTenantResponse>.Forbid(
                "Only interactive SuperAdmin users can create tenants.");
        }

        var currentTenant = tenantContextAccessor.CurrentTenant;
        if (currentTenant is null)
        {
            return Result<DuplicateTenantResponse>.Forbid(
                "Tenant context is required to create a tenant.");
        }

        if (currentTenant.Id != request.SourceTenantId)
        {
            return Result<DuplicateTenantResponse>.Forbid(
                $"Cannot create a tenant from '{request.SourceTenantId}'. " +
                $"Administrators may only start from their own tenant ('{currentTenant.Id}').");
        }

        if (request.NewTenantId == request.SourceTenantId)
        {
            return Result<DuplicateTenantResponse>.Validation(
                "New tenant id must differ from the current tenant id.");
        }

        if (!TryDecodePayload(request.PayloadJson, out var payload, out var payloadError))
        {
            return Result<DuplicateTenantResponse>.Validation(payloadError);
        }

        var source = await settingsQuery.ListSettingsAsync(request.SourceTenantId, cancellationToken);
        if (source is null)
        {
            return Result<DuplicateTenantResponse>.NotFound(
                $"Tenant '{request.SourceTenantId}' was not found.");
        }

        var errors = new List<string>();
        var settings = await BuildSettingsAsync(
            request.NewTenantId,
            payload.Settings,
            source.Settings,
            errors,
            cancellationToken);
        if (errors.Count == 0)
        {
            errors.AddRange(NewTenantSettingsRequirements.Validate(settings));
        }

        if (errors.Count > 0)
        {
            return Result<DuplicateTenantResponse>.Validation(string.Join(" ", errors));
        }

        try
        {
            var result = await tenantCreator.CreateAsync(
                new NewTenantDefinition(
                    request.NewTenantId,
                    request.NewTenantName,
                    payload.Hostname,
                    payload.FrontendOrigin,
                    settings),
                cancellationToken);

            await tenantConfigProvider.RefreshAsync(cancellationToken);

            return Result<DuplicateTenantResponse>.Success(
                new DuplicateTenantResponse(
                    request.SourceTenantId,
                    result.TenantId,
                    result.Name,
                    result.Hostname,
                    result.FrontendOrigin,
                    result.SettingsCreated,
                    $"Tenant '{result.Name}' created with {result.SettingsCreated} setting(s). " +
                    "Create a template and add users before using the new tenant."));
        }
        catch (InvalidOperationException ex)
        {
            return Result<DuplicateTenantResponse>.Validation(ex.Message);
        }
    }

    private async Task<List<NewTenantSetting>> BuildSettingsAsync(
        Guid newTenantId,
        IReadOnlyList<NewTenantPayloadSetting> requested,
        IReadOnlyCollection<TenantSettingRow> sourceRows,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var settings = new List<NewTenantSetting>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var isProduction = TestAuthenticationEnvironmentGate.IsProduction(hostEnvironment);

        foreach (var item in requested)
        {
            var category = (item.Category ?? string.Empty).Trim();
            var target = Targets.FirstOrDefault(t =>
                string.Equals(t, (item.Target ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
            var label = $"'{category}' ({item.Target})";

            if (!NewTenantSettingsRequirements.Categories.Contains(category))
            {
                errors.Add($"{label} cannot be set when creating a tenant. Add it in Tenant Settings after the tenant exists.");
                continue;
            }

            if (target is null)
            {
                errors.Add($"{label}: Target must be Shared, Api or Web.");
                continue;
            }

            if (!seen.Add($"{category}|{target}"))
            {
                errors.Add($"{label} is listed more than once.");
                continue;
            }

            var json = item.SettingsJson ?? string.Empty;
            var sourceRow = sourceRows.FirstOrDefault(s =>
                string.Equals(s.Category, category, StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.Target, target, StringComparison.OrdinalIgnoreCase));

            if (json.Contains(TenantSettingSecretJson.Sentinel, StringComparison.Ordinal))
            {
                if (sourceRow is null)
                {
                    errors.Add($"{label} uses {TenantSettingSecretJson.Sentinel} but the current tenant has no {category} ({target}) setting to copy it from.");
                    continue;
                }

                var restored = TenantSettingSecretJson.Restore(json, sourceRow.SettingsJson);
                if (restored.Errors.Count > 0)
                {
                    errors.AddRange(restored.Errors.Select(e => $"{label}: {e}"));
                    continue;
                }

                json = restored.Json;
            }

            var validationErrors = TenantSettingJsonValidator.Validate(category, target, json);
            if (validationErrors.Count > 0)
            {
                errors.AddRange(validationErrors.Select(e => $"{label}: {e}"));
                continue;
            }

            if (isProduction && UpsertTenantSettingCommandHandler.WouldEnableTestAuthentication(category, json))
            {
                errors.Add($"{label}: Test Authentication cannot be enabled or selected in Production.");
                continue;
            }

            // Copied HostMappings usually point at the current tenant's own templates, which the
            // new tenant may not claim (same rule as Tenant Settings).
            var ownershipErrors = await templateMappingOwnershipValidator.ValidateAsync(
                newTenantId,
                category,
                json,
                cancellationToken);
            if (ownershipErrors.Count > 0)
            {
                errors.AddRange(ownershipErrors.Select(e => $"{label}: {e}"));
                continue;
            }

            var isSecret = TenantSettingsSecretCategories.ShouldEncrypt(category)
                           || sourceRow?.IsSecret == true
                           || TenantSettingSecretJson.Redact(json, category).Values.Count > 0;

            settings.Add(new NewTenantSetting(category, target, json, isSecret));
        }

        return settings;
    }

    internal static bool TryDecodePayload(
        string payloadJsonBase64,
        out NewTenantPayload payload,
        out string error)
    {
        payload = new NewTenantPayload();
        error = string.Empty;

        if (!WafSafeUtf8Base64.TryDecode(payloadJsonBase64, out var json, out var decodeError))
        {
            error = $"PayloadJson: {decodeError}";
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<NewTenantPayload>(json, PayloadJsonOptions);
            if (parsed is null)
            {
                error = "PayloadJson did not contain a new tenant object.";
                return false;
            }

            payload = parsed;
        }
        catch (JsonException ex)
        {
            error = $"PayloadJson is not valid new tenant JSON: {ex.Message}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(payload.Hostname))
        {
            error = "hostname is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(payload.FrontendOrigin))
        {
            error = "frontendOrigin is required.";
            return false;
        }

        payload.Settings ??= [];
        if (payload.Settings.Count == 0)
        {
            error = "settings must contain the new tenant's settings.";
            return false;
        }

        return true;
    }
}
