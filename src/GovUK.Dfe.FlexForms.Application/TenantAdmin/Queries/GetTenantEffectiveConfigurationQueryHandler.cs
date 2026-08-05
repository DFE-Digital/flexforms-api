using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;
using Microsoft.Extensions.Configuration;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin.Queries;

public sealed record GetTenantEffectiveConfigurationQuery(Guid TenantId)
    : IRequest<Result<TenantEffectiveConfigurationDto>>;

public sealed class GetTenantEffectiveConfigurationQueryHandler(
    ITenantContextAccessor tenantContextAccessor,
    IPermissionCheckerService permissionChecker,
    ITenantConfigurationProvider tenantConfigProvider,
    ITenantConfigurationRefreshState? refreshState,
    ITenantAuthProviderRegistry authProviderRegistry,
    ITenantHostnameResolver hostnameResolver,
    ITenantSettingsReader settingsReader)
    : IRequestHandler<GetTenantEffectiveConfigurationQuery, Result<TenantEffectiveConfigurationDto>>
{
    public async Task<Result<TenantEffectiveConfigurationDto>> Handle(
        GetTenantEffectiveConfigurationQuery request,
        CancellationToken cancellationToken)
    {
        var isSuperAdmin = permissionChecker.IsInteractivePlatformAdmin();
        if (!isSuperAdmin && !permissionChecker.IsInteractiveTenantAdmin())
        {
            return Result<TenantEffectiveConfigurationDto>.Forbid(
                "Only interactive Admin users can view tenant configuration.");
        }

        var currentTenant = tenantContextAccessor.CurrentTenant;
        if (currentTenant is null || currentTenant.Id != request.TenantId)
        {
            return Result<TenantEffectiveConfigurationDto>.Forbid(
                "Administrators may only view effective configuration for their own tenant.");
        }

        var webSnapshot = await settingsReader.GetConfigurationAsync(request.TenantId, "Web", cancellationToken);
        var webSettings = BuildConfiguration(webSnapshot?.Configuration);

        var scheme = TenantInteractiveAuthSchemeResolver.ResolveSchemeName(webSettings);
        var testEnabled = TenantInteractiveAuthSchemeResolver.GetTestAuthenticationEnabled(webSettings);
        var entraEnabled = TenantInteractiveAuthSchemeResolver.GetEntraSsoEnabled(webSettings);
        var dsiConfigured = TenantInteractiveAuthSchemeResolver.IsDfESignInConfigured(webSettings);

        var hostnames = await hostnameResolver.ListHostnamesForTenantAsync(request.TenantId, cancellationToken);
        var origins = currentTenant.FrontendOrigins ?? [];

        var dto = new TenantEffectiveConfigurationDto(
            request.TenantId,
            currentTenant.Name,
            tenantConfigProvider.Source,
            isSuperAdmin ? refreshState?.LastRefreshedUtc : null,
            isSuperAdmin ? refreshState?.ActiveTenantCount ?? tenantConfigProvider.GetAllTenants().Count : 0,
            scheme,
            testEnabled,
            entraEnabled,
            dsiConfigured,
            isSuperAdmin ? authProviderRegistry.GetAll().Count : 0,
            hostnames,
            isSuperAdmin ? origins : []);

        return Result<TenantEffectiveConfigurationDto>.Success(dto);
    }

    private static IConfiguration BuildConfiguration(IReadOnlyDictionary<string, string?>? pairs)
    {
        if (pairs is null || pairs.Count == 0)
        {
            return new ConfigurationBuilder().Build();
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(pairs)
            .Build();
    }
}
