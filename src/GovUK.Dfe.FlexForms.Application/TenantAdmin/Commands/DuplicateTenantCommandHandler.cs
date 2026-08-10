using FluentValidation;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Common;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin.Commands;

/// <param name="AuthorizationApiSecretKey">
/// Actual Authorization SecretKey, Base64-encoded UTF-8 (WAF-safe transport).
/// </param>
/// <param name="InternalServiceAuthSecretKey">
/// Actual InternalServiceAuth SecretKey, Base64-encoded UTF-8 (WAF-safe transport).
/// </param>
/// <param name="InternalServiceAuthServiceApiKeys">
/// Service emails with ApiKeys Base64-encoded UTF-8 (WAF-safe transport).
/// </param>
public sealed record DuplicateTenantCommand(
    Guid SourceTenantId,
    Guid NewTenantId,
    string NewTenantName,
    string Hostname,
    string FrontendOrigin,
    string AuthorizationApiSecretKey,
    string InternalServiceAuthSecretKey,
    IReadOnlyList<(string Email, string ApiKey)> InternalServiceAuthServiceApiKeys)
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
        RuleFor(x => x.AuthorizationApiSecretKey)
            .NotEmpty()
            .Must(WafSafeUtf8Base64.IsValidBase64)
            .WithMessage("AuthorizationApiSecretKey must be a valid Base64-encoded UTF-8 string.")
            .Must(encoded => DecodedLengthAtLeast(encoded, 32))
            .WithMessage("AuthorizationApiSecretKey must decode to at least 32 characters.");
        RuleFor(x => x.InternalServiceAuthSecretKey)
            .NotEmpty()
            .Must(WafSafeUtf8Base64.IsValidBase64)
            .WithMessage("InternalServiceAuthSecretKey must be a valid Base64-encoded UTF-8 string.")
            .Must(encoded => DecodedLengthAtLeast(encoded, 32))
            .WithMessage("InternalServiceAuthSecretKey must decode to at least 32 characters.");
        RuleForEach(x => x.InternalServiceAuthServiceApiKeys).ChildRules(service =>
        {
            service.RuleFor(s => s.Email).NotEmpty();
            service.RuleFor(s => s.ApiKey)
                .NotEmpty()
                .Must(WafSafeUtf8Base64.IsValidBase64)
                .WithMessage("ApiKey must be a valid Base64-encoded UTF-8 string.")
                .Must(encoded => DecodedLengthAtLeast(encoded, 32))
                .WithMessage("ApiKey must decode to at least 32 characters.");
        });
    }

    private static bool DecodedLengthAtLeast(string encoded, int minLength) =>
        WafSafeUtf8Base64.TryDecode(encoded, out var decoded, out _)
        && decoded.Length >= minLength;
}

/// <summary>
/// Creates a new TenantConfig tenant by copying settings from the caller's current tenant.
/// Interactive SuperAdmin only. Principals are not copied.
/// Secret fields on the command are Base64-encoded UTF-8 (WAF-safe); decoded before persistence.
/// </summary>
public sealed class DuplicateTenantCommandHandler(
    ITenantDuplicator tenantDuplicator,
    ITenantContextAccessor tenantContextAccessor,
    IPermissionCheckerService permissionChecker,
    ITenantConfigurationProvider tenantConfigProvider)
    : IRequestHandler<DuplicateTenantCommand, Result<DuplicateTenantResponse>>
{
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

        if (!WafSafeUtf8Base64.TryDecode(
                request.AuthorizationApiSecretKey,
                out var authorizationApiSecretKey,
                out var authError))
        {
            return Result<DuplicateTenantResponse>.Validation(
                $"AuthorizationApiSecretKey: {authError}");
        }

        if (!WafSafeUtf8Base64.TryDecode(
                request.InternalServiceAuthSecretKey,
                out var internalServiceAuthSecretKey,
                out var internalAuthError))
        {
            return Result<DuplicateTenantResponse>.Validation(
                $"InternalServiceAuthSecretKey: {internalAuthError}");
        }

        var decodedServiceApiKeys = new List<(string Email, string ApiKey)>(
            request.InternalServiceAuthServiceApiKeys.Count);
        foreach (var (email, apiKey) in request.InternalServiceAuthServiceApiKeys)
        {
            if (!WafSafeUtf8Base64.TryDecode(apiKey, out var decodedApiKey, out var apiKeyError))
            {
                return Result<DuplicateTenantResponse>.Validation(
                    $"InternalServiceAuthServiceApiKeys ApiKey for '{email}': {apiKeyError}");
            }

            decodedServiceApiKeys.Add((email, decodedApiKey));
        }

        try
        {
            var result = await tenantDuplicator.DuplicateAsync(
                request.SourceTenantId,
                request.NewTenantId,
                request.NewTenantName,
                request.Hostname,
                request.FrontendOrigin,
                authorizationApiSecretKey,
                internalServiceAuthSecretKey,
                decodedServiceApiKeys,
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
}
