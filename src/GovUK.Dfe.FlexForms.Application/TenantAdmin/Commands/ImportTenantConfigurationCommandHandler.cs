using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Application.TenantAdmin.Validation;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin.Commands;

public sealed record ImportTenantConfigurationCommand(
    Guid TenantId,
    ImportTenantConfigurationDto Bundle) : IRequest<Result<ImportTenantConfigurationResultDto>>;

public sealed class ImportTenantConfigurationCommandHandler(
    ITenantContextAccessor tenantContextAccessor,
    IPermissionCheckerService permissionChecker,
    ITenantConfigurationPromotion promotionService,
    ITenantConfigurationProvider tenantConfigProvider,
    IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<ImportTenantConfigurationCommand, Result<ImportTenantConfigurationResultDto>>
{
    public async Task<Result<ImportTenantConfigurationResultDto>> Handle(
        ImportTenantConfigurationCommand request,
        CancellationToken cancellationToken)
    {
        if (!permissionChecker.IsInteractivePlatformAdmin())
        {
            return Result<ImportTenantConfigurationResultDto>.Forbid(
                "Only interactive SuperAdmin users can import tenant configuration.");
        }

        var currentTenant = tenantContextAccessor.CurrentTenant;
        if (currentTenant is null || currentTenant.Id != request.TenantId)
        {
            return Result<ImportTenantConfigurationResultDto>.Forbid(
                "Administrators may only import configuration for their own tenant.");
        }

        if (request.Bundle.Settings.Count == 0)
        {
            return Result<ImportTenantConfigurationResultDto>.Validation(
                "At least one setting is required.");
        }

        var validationErrors = new List<string>();

        foreach (var item in request.Bundle.Settings)
        {
            // Export redacts secrets as {"__SECRET__":true} — skip regardless of IsSecret flag.
            if (request.Bundle.SkipSecretPlaceholders
                && TenantSettingJsonValidator.IsSecretPlaceholder(item.SettingsJson))
            {
                continue;
            }

            var errors = TenantSettingJsonValidator.Validate(
                item.Category,
                item.Target,
                item.SettingsJson,
                TenantSettingValidationMode.Lenient);

            foreach (var error in errors)
            {
                validationErrors.Add($"{item.Category}/{item.Target}: {error}");
            }
        }

        if (validationErrors.Count > 0)
        {
            return Result<ImportTenantConfigurationResultDto>.Validation(
                string.Join(" ", validationErrors));
        }

        var result = await promotionService.ImportAsync(
            request.TenantId,
            request.Bundle,
            ResolveActorEmail(),
            cancellationToken);

        await tenantConfigProvider.RefreshAsync(cancellationToken);

        return Result<ImportTenantConfigurationResultDto>.Success(result);
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
