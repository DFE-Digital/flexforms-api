using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MediatR;
using Microsoft.Extensions.Configuration;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin.Queries;

public sealed record GetTenantHealthQuery(Guid TenantId) : IRequest<Result<TenantHealthDto>>;

public sealed class GetTenantHealthQueryHandler(
    ITenantContextAccessor tenantContextAccessor,
    IPermissionCheckerService permissionChecker,
    ITenantConfigurationProvider tenantConfigProvider,
    ITenantConfigurationRefreshState? refreshState,
    ITenantAuthProviderRegistry authProviderRegistry,
    ITenantHostnameResolver hostnameResolver,
    ITenantSettingsReader settingsReader,
    ITenantSettingsQuery settingsQuery)
    : IRequestHandler<GetTenantHealthQuery, Result<TenantHealthDto>>
{
    public async Task<Result<TenantHealthDto>> Handle(
        GetTenantHealthQuery request,
        CancellationToken cancellationToken)
    {
        if (!permissionChecker.IsInteractiveTenantAdmin())
        {
            return Result<TenantHealthDto>.Forbid(
                "Only interactive tenant administrators can view tenant health.");
        }

        var currentTenant = tenantContextAccessor.CurrentTenant;
        if (currentTenant is null || currentTenant.Id != request.TenantId)
        {
            return Result<TenantHealthDto>.Forbid(
                "Administrators may only view health for their own tenant.");
        }

        var webSnapshot = await settingsReader.GetConfigurationAsync(request.TenantId, "Web", cancellationToken);
        var webSettings = BuildConfiguration(webSnapshot?.Configuration);
        var scheme = TenantInteractiveAuthSchemeResolver.ResolveSchemeName(webSettings);
        var testEnabled = TenantInteractiveAuthSchemeResolver.GetTestAuthenticationEnabled(webSettings);
        var entraEnabled = TenantInteractiveAuthSchemeResolver.GetEntraSsoEnabled(webSettings);
        var dsiConfigured = TenantInteractiveAuthSchemeResolver.IsDfESignInConfigured(webSettings);

        var hostnames = await hostnameResolver.ListHostnamesForTenantAsync(request.TenantId, cancellationToken);
        var origins = currentTenant.FrontendOrigins ?? [];
        var settingsList = await settingsQuery.ListSettingsAsync(request.TenantId, cancellationToken);
        var settingCount = settingsList?.Settings.Count ?? 0;

        var effective = new TenantEffectiveConfigurationDto(
            request.TenantId,
            currentTenant.Name,
            tenantConfigProvider.Source,
            refreshState?.LastRefreshedUtc,
            refreshState?.ActiveTenantCount ?? tenantConfigProvider.GetAllTenants().Count,
            scheme,
            testEnabled,
            entraEnabled,
            dsiConfigured,
            authProviderRegistry.GetAll().Count,
            hostnames,
            origins);

        var checks = new List<TenantHealthCheckDto>
        {
            Check("config-source", "Config source",
                string.Equals(effective.ConfigSource, "Database", StringComparison.OrdinalIgnoreCase)
                    ? ("Pass", $"Using {effective.ConfigSource}")
                    : ("Warn", $"Using {effective.ConfigSource} (expected Database in production)")),

            Check("settings-present", "Settings loaded",
                settingCount > 0
                    ? ("Pass", $"{settingCount} setting rows")
                    : ("Fail", "No TenantConfig settings found")),

            Check("hostname", "Hostname mapping",
                hostnames.Count > 0
                    ? ("Pass", string.Join(", ", hostnames))
                    : ("Fail", "No hostnames configured")),

            Check("cors", "CORS origins",
                origins.Length > 0
                    ? ("Pass", string.Join(", ", origins))
                    : ("Warn", "No frontend origins configured")),

            Check("auth-scheme", "Interactive auth scheme",
                BuildAuthCheck(scheme, testEnabled, entraEnabled, dsiConfigured)),

            Check("catalogue-refresh", "Catalogue refresh",
                effective.LastCatalogueRefreshUtc is { } refreshed
                    ? (DateTimeOffset.UtcNow - refreshed < TimeSpan.FromMinutes(10)
                        ? ("Pass", $"Last refresh {refreshed:u}")
                        : ("Warn", $"Last refresh {refreshed:u} (stale?)"))
                    : ("Warn", "Catalogue has never been refreshed")),
        };

        var overall = checks.Any(c => c.Status == "Fail") ? "Fail"
            : checks.Any(c => c.Status == "Warn") ? "Warn"
            : "Pass";

        return Result<TenantHealthDto>.Success(
            new TenantHealthDto(request.TenantId, currentTenant.Name, overall, checks, effective));
    }

    private static TenantHealthCheckDto Check(string code, string label, (string Status, string Detail) result)
        => new(code, label, result.Status, result.Detail);

    private static (string Status, string Detail) BuildAuthCheck(
        string scheme, bool testEnabled, bool entraEnabled, bool dsiConfigured)
    {
        var enabledCount = (testEnabled ? 1 : 0) + (entraEnabled ? 1 : 0) + (dsiConfigured ? 1 : 0);
        if (string.IsNullOrWhiteSpace(scheme))
        {
            return ("Fail", "No interactive auth scheme resolved");
        }

        if (enabledCount > 1 && !string.Equals(scheme, "TestAuthentication", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(scheme, "EntraSso", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(scheme, "DfESignIn", StringComparison.OrdinalIgnoreCase))
        {
            return ("Warn", $"Unresolved scheme '{scheme}' with multiple providers configured");
        }

        if (testEnabled && string.Equals(scheme, "TestAuthentication", StringComparison.OrdinalIgnoreCase))
        {
            return ("Warn", "TestAuthentication is the active interactive scheme");
        }

        if (enabledCount > 1)
        {
            return ("Warn",
                $"Active scheme is {scheme}, but multiple providers are configured — set Authentication:Scheme explicitly");
        }

        return ("Pass", $"Active scheme: {scheme}");
    }

    private static IConfiguration BuildConfiguration(IReadOnlyDictionary<string, string?>? pairs)
    {
        if (pairs is null || pairs.Count == 0)
        {
            return new ConfigurationBuilder().Build();
        }

        return new ConfigurationBuilder().AddInMemoryCollection(pairs).Build();
    }
}
