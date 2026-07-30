using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin.Commands;

public sealed record UpsertTenantSettingCommand(
    Guid TenantId,
    string Category,
    string Target,
    string SettingsJson,
    bool IsSecret) : IRequest<Result<UpsertTenantSettingResponse>>;

/// <summary>
/// Upserts a TenantConfig settings category for a tenant.
/// Callers must be tenant Admins and may only mutate the tenant resolved for the current request.
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
        if (!permissionChecker.IsInteractiveTenantAdmin())
        {
            return Result<UpsertTenantSettingResponse>.Forbid(
                "Only interactive Admin users (user JWT) can update tenant settings. Client-credentials / service tokens are not allowed.");
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

        try
        {
            var result = await settingsWriter.UpsertSettingAsync(
                request.TenantId,
                request.Category,
                request.Target,
                request.SettingsJson,
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
