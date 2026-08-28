using System.Collections.Concurrent;
using GovUK.Dfe.CoreLibs.FileStorage.Interfaces;
using GovUK.Dfe.CoreLibs.FileStorage.Services;
using GovUK.Dfe.CoreLibs.FileStorage.Settings;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Caches per-tenant <see cref="AzureFileStorageService"/> instances keyed by tenant id + connection + share.
/// Host GlobalConfiguration Azure values are not used for these clients.
/// </summary>
public sealed class TenantAzureFileStorageFactory(
    IHttpContextAccessor httpContextAccessor,
    ILogger<TenantAzureFileStorageFactory> logger) : ITenantAzureFileStorageFactory
{
    private readonly ConcurrentDictionary<string, AzureFileStorageService> _cache = new(StringComparer.Ordinal);

    public IFileStorageService? GetAzureFileStorageOrNull() => GetOrCreateOptional();

    public IAzureSpecificOperations? GetAzureOperationsOrNull() => GetOrCreateOptional();

    public IFileStorageService GetRequiredAzureFileStorage()
    {
        var tenant = GetCurrentTenant();
        var provider = tenant.Settings.GetValue<string>("FileStorage:Provider")
            ?? throw new InvalidOperationException(
                $"Tenant '{tenant.Name}' has no FileStorage:Provider configured.");

        if (!string.Equals(provider, "Azure", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Tenant '{tenant.Name}' FileStorage Provider is '{provider}', which does not use Azure File Share for uploads.");
        }

        return GetOrCreate(tenant, provider);
    }

    private AzureFileStorageService? GetOrCreateOptional()
    {
        var tenant = GetCurrentTenant();
        var provider = tenant.Settings.GetValue<string>("FileStorage:Provider");
        if (!UsesTenantAzureClient(provider))
        {
            return null;
        }

        return GetOrCreate(tenant, provider!);
    }

    private AzureFileStorageService GetOrCreate(TenantConfiguration tenant, string provider)
    {
        var connectionString = tenant.Settings.GetValue<string>("FileStorage:Azure:ConnectionString");
        var shareName = tenant.Settings.GetValue<string>("FileStorage:Azure:ShareName");

        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(shareName))
        {
            throw new InvalidOperationException(
                $"Tenant '{tenant.Name}' FileStorage Provider is {provider} but FileStorage:Azure:ConnectionString and/or ShareName is missing.");
        }

        var cacheKey = $"{tenant.Id:N}|{shareName}|{connectionString}";
        return _cache.GetOrAdd(cacheKey, _ =>
        {
            logger.LogInformation(
                "Creating Azure File Share client for tenant {TenantName} (share {ShareName})",
                tenant.Name,
                shareName);

            var options = new FileStorageOptions
            {
                Provider = "Azure",
                Azure = new AzureFileStorageOptions
                {
                    ConnectionString = connectionString,
                    ShareName = shareName
                }
            };

            return new AzureFileStorageService(options);
        });
    }

    private TenantConfiguration GetCurrentTenant()
    {
        var requestServices = httpContextAccessor.HttpContext?.RequestServices
            ?? throw new InvalidOperationException("No HttpContext available for tenant Azure file storage.");

        var tenantContextAccessor = requestServices.GetRequiredService<ITenantContextAccessor>();
        return tenantContextAccessor.CurrentTenant
            ?? throw new InvalidOperationException("No tenant context available for Azure file storage.");
    }

    private static bool UsesTenantAzureClient(string? provider) =>
        string.Equals(provider, "Azure", StringComparison.OrdinalIgnoreCase)
        || string.Equals(provider, "Hybrid", StringComparison.OrdinalIgnoreCase);
}
