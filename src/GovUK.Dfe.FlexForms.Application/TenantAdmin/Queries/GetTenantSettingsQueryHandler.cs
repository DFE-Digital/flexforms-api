using FluentValidation;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.Security;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;
using Microsoft.Extensions.Hosting;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin.Queries;

public sealed record GetTenantSettingsQuery(Guid TenantId)
    : IRequest<Result<GetTenantSettingsResponse>>;

internal class GetTenantSettingsQueryValidator : AbstractValidator<GetTenantSettingsQuery>
{
    public GetTenantSettingsQueryValidator()
    {
        RuleFor(x => x.TenantId)
            .NotEmpty()
            .WithMessage("Tenant ID is required.");
    }
}

/// <summary>
/// Lists TenantConfig settings for the current tenant.
/// Secret leaves are redacted except for interactive SuperAdmin in Dev/Test environments.
/// </summary>
public sealed class GetTenantSettingsQueryHandler(
    ITenantSettingsQuery settingsQuery,
    ITenantContextAccessor tenantContextAccessor,
    IPermissionCheckerService permissionChecker,
    IHostEnvironment hostEnvironment)
    : IRequestHandler<GetTenantSettingsQuery, Result<GetTenantSettingsResponse>>
{
    public async Task<Result<GetTenantSettingsResponse>> Handle(
        GetTenantSettingsQuery request,
        CancellationToken cancellationToken)
    {
        if (!permissionChecker.IsInteractiveTenantAdmin())
        {
            return Result<GetTenantSettingsResponse>.Forbid(
                "Only interactive tenant administrators can view tenant settings.");
        }

        var currentTenant = tenantContextAccessor.CurrentTenant;
        if (currentTenant is null)
        {
            return Result<GetTenantSettingsResponse>.Forbid(
                "Tenant context is required to view tenant settings.");
        }

        if (currentTenant.Id != request.TenantId)
        {
            return Result<GetTenantSettingsResponse>.Forbid(
                $"Cannot view settings for tenant '{request.TenantId}'. " +
                $"Administrators may only view their own tenant ('{currentTenant.Id}').");
        }

        var list = await settingsQuery.ListSettingsAsync(request.TenantId, cancellationToken);
        if (list is null)
            return Result<GetTenantSettingsResponse>.NotFound($"Tenant '{request.TenantId}' was not found.");

        var includePlaintext = TenantSettingSecretPlaintextGate.AllowsSuperAdminPlaintext(
            hostEnvironment,
            permissionChecker.IsInteractivePlatformAdmin());

        var response = new GetTenantSettingsResponse(
            list.TenantId,
            list.TenantName,
            list.Settings.Select(s => TenantSettingSecretJson.ToAdminDto(s, includePlaintext)).ToList());

        return Result<GetTenantSettingsResponse>.Success(response);
    }
}
