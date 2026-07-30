using System.Text;
using FluentValidation;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;

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
/// Callers must be interactive SuperAdmins and may only mutate the tenant resolved for the current request.
/// </summary>
public sealed class UpsertTenantSettingCommandHandler(
    ITenantSettingsWriter settingsWriter,
    ITenantContextAccessor tenantContextAccessor,
    IPermissionCheckerService permissionChecker,
    ITenantConfigurationProvider tenantConfigProvider)
    : IRequestHandler<UpsertTenantSettingCommand, Result<UpsertTenantSettingResponse>>
{
    public async Task<Result<UpsertTenantSettingResponse>> Handle(
        UpsertTenantSettingCommand request,
        CancellationToken cancellationToken)
    {
        if (!permissionChecker.IsInteractivePlatformAdmin())
        {
            return Result<UpsertTenantSettingResponse>.Forbid(
                "Only interactive SuperAdmin users can update tenant settings. Client-credentials / service tokens are not allowed.");
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

        try
        {
            var result = await settingsWriter.UpsertSettingAsync(
                request.TenantId,
                request.Category,
                request.Target,
                decodedSettingsJson,
                request.IsSecret,
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
}
