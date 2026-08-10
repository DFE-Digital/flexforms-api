using System.Text.Json;
using FluentValidation;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Common;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin.Commands;

/// <param name="PayloadJson">
/// Base64-encoded UTF-8 JSON containing authorization and InternalServiceAuth secrets
/// (WAF-safe transport; see <see cref="CloneTenantSecretsPayload"/>).
/// </param>
public sealed record DuplicateTenantCommand(
    Guid SourceTenantId,
    Guid NewTenantId,
    string NewTenantName,
    string Hostname,
    string FrontendOrigin,
    string PayloadJson)
    : IRequest<Result<DuplicateTenantResponse>>;

internal sealed class DuplicateTenantCommandValidator : AbstractValidator<DuplicateTenantCommand>
{
    public DuplicateTenantCommandValidator()
    {
        RuleFor(x => x.SourceTenantId).NotEmpty();
        RuleFor(x => x.NewTenantId).NotEmpty();
        RuleFor(x => x.NewTenantName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Hostname).NotEmpty().MaximumLength(255);
        RuleFor(x => x.FrontendOrigin).NotEmpty().MaximumLength(500);
        RuleFor(x => x.PayloadJson)
            .NotEmpty()
            .Must(WafSafeUtf8Base64.IsValidBase64)
            .WithMessage("PayloadJson must be a valid Base64-encoded UTF-8 string.");
    }
}

/// <summary>
/// Creates a new TenantConfig tenant by copying settings from the caller's current tenant.
/// Interactive SuperAdmin only. Principals are not copied.
/// </summary>
public sealed class DuplicateTenantCommandHandler(
    ITenantDuplicator tenantDuplicator,
    ITenantContextAccessor tenantContextAccessor,
    IPermissionCheckerService permissionChecker,
    ITenantConfigurationProvider tenantConfigProvider)
    : IRequestHandler<DuplicateTenantCommand, Result<DuplicateTenantResponse>>
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<Result<DuplicateTenantResponse>> Handle(
        DuplicateTenantCommand request,
        CancellationToken cancellationToken)
    {
        if (!permissionChecker.IsInteractivePlatformAdmin())
        {
            return Result<DuplicateTenantResponse>.Forbid(
                "Only interactive SuperAdmin users can duplicate tenants.");
        }

        var currentTenant = tenantContextAccessor.CurrentTenant;
        if (currentTenant is null)
        {
            return Result<DuplicateTenantResponse>.Forbid(
                "Tenant context is required to duplicate a tenant.");
        }

        if (currentTenant.Id != request.SourceTenantId)
        {
            return Result<DuplicateTenantResponse>.Forbid(
                $"Cannot duplicate tenant '{request.SourceTenantId}'. " +
                $"Administrators may only duplicate their own tenant ('{currentTenant.Id}').");
        }

        if (!TryDecodeSecretsPayload(
                request.PayloadJson,
                out var secrets,
                out var payloadError))
        {
            return Result<DuplicateTenantResponse>.Validation(payloadError);
        }

        try
        {
            var result = await tenantDuplicator.DuplicateAsync(
                request.SourceTenantId,
                request.NewTenantId,
                request.NewTenantName,
                request.Hostname,
                request.FrontendOrigin,
                secrets.AuthorizationApiSecretKey,
                secrets.InternalServiceAuthSecretKey,
                secrets.InternalServiceAuthServiceApiKeys
                    .Select(s => (s.Email, s.ApiKey))
                    .ToList(),
                cancellationToken);

            await tenantConfigProvider.RefreshAsync(cancellationToken);

            return Result<DuplicateTenantResponse>.Success(
                new DuplicateTenantResponse(
                    result.SourceTenantId,
                    result.NewTenantId,
                    result.NewTenantName,
                    result.Hostname,
                    result.FrontendOrigin,
                    result.SettingsCopied,
                    $"Tenant '{result.NewTenantName}' created with {result.SettingsCopied} setting(s). " +
                    "Review secrets, identity providers and principals before using the new tenant in production."));
        }
        catch (KeyNotFoundException ex)
        {
            return Result<DuplicateTenantResponse>.NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Result<DuplicateTenantResponse>.Validation(ex.Message);
        }
    }

    internal static bool TryDecodeSecretsPayload(
        string payloadJsonBase64,
        out CloneTenantSecretsPayload secrets,
        out string error)
    {
        secrets = new CloneTenantSecretsPayload();
        error = string.Empty;

        if (!WafSafeUtf8Base64.TryDecode(payloadJsonBase64, out var json, out var decodeError))
        {
            error = $"PayloadJson: {decodeError}";
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<CloneTenantSecretsPayload>(json, PayloadJsonOptions);
            if (parsed is null)
            {
                error = "PayloadJson did not contain a secrets object.";
                return false;
            }

            secrets = parsed;
        }
        catch (JsonException ex)
        {
            error = $"PayloadJson is not valid secrets JSON: {ex.Message}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(secrets.AuthorizationApiSecretKey)
            || secrets.AuthorizationApiSecretKey.Length < 32)
        {
            error = "authorizationApiSecretKey must be at least 32 characters.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(secrets.InternalServiceAuthSecretKey)
            || secrets.InternalServiceAuthSecretKey.Length < 32)
        {
            error = "internalServiceAuthSecretKey must be at least 32 characters.";
            return false;
        }

        secrets.InternalServiceAuthServiceApiKeys ??= [];
        foreach (var service in secrets.InternalServiceAuthServiceApiKeys)
        {
            if (string.IsNullOrWhiteSpace(service.Email))
            {
                error = "Each service api key entry requires an email.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(service.ApiKey) || service.ApiKey.Length < 32)
            {
                error = $"ApiKey for '{service.Email}' must be at least 32 characters.";
                return false;
            }
        }

        return true;
    }
}
