using GovUK.Dfe.CoreLibs.FileStorage.Interfaces;
using GovUK.Dfe.CoreLibs.FileStorage.Services;
using GovUK.Dfe.CoreLibs.FileStorage.Settings;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Builds per-tenant disk <see cref="LocalFileStorageService"/> from TenantConfig
/// <c>FileStorage:Local</c> (e.g. Container Apps mount at <c>/uploads</c>).
/// Does not use host GlobalConfiguration BaseDirectory — that path often resolves to
/// <c>/app/uploads</c> and fails in the CoreLibs constructor before any override runs.
/// </summary>
public interface ITenantDiskFileStorageFactory
{
    /// <summary>
    /// Local disk service for the current tenant (Local or Hybrid Provider).
    /// </summary>
    IFileStorageService GetRequiredDiskFileStorage();
}

public sealed class TenantDiskFileStorageFactory(
    IHttpContextAccessor httpContextAccessor,
    ILogger<TenantDiskFileStorageFactory> logger) : ITenantDiskFileStorageFactory
{
    private readonly ConcurrentDictionary<string, LocalFileStorageService> _cache = new(StringComparer.Ordinal);

    public IFileStorageService GetRequiredDiskFileStorage()
    {
        var tenant = GetCurrentTenant();
        var provider = tenant.Settings.GetValue<string>("FileStorage:Provider")
            ?? throw new InvalidOperationException(
                $"Tenant '{tenant.Name}' has no FileStorage:Provider configured.");

        if (!string.Equals(provider, "Local", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(provider, "Hybrid", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Tenant '{tenant.Name}' FileStorage Provider is '{provider}', which does not use local disk storage.");
        }

        var baseDirectory = tenant.Settings.GetValue<string>("FileStorage:Local:BaseDirectory");
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            throw new InvalidOperationException(
                $"Tenant '{tenant.Name}' FileStorage Provider is {provider} but FileStorage:Local:BaseDirectory is missing. " +
                "For Container Apps with an Azure Files mount, set BaseDirectory to the mount path (e.g. /uploads).");
        }

        var createIfMissing = tenant.Settings.GetValue<bool?>("FileStorage:Local:CreateDirectoryIfNotExists") ?? true;
        var allowOverwrite = tenant.Settings.GetValue<bool?>("FileStorage:Local:AllowOverwrite") ?? true;
        var maxBytes = tenant.Settings.GetValue<long?>("FileStorage:Local:MaxFileSizeBytes") ?? 100 * 1024 * 1024;
        var extensions = tenant.Settings.GetSection("FileStorage:Local:AllowedExtensions").Get<string[]>() ?? Array.Empty<string>();
        var pattern = tenant.Settings.GetValue<string>("FileStorage:Local:AllowedFileNamePattern");
        var patternFriendly = tenant.Settings.GetValue<string>("FileStorage:Local:AllowedFileNamePatternFriendlyList")
            ?? "a-z A-Z 0-9 _ - no-space";
        var extensionsFriendly = tenant.Settings.GetValue<string>("FileStorage:Local:AllowedExtensionsFriendlyList");
        if (string.IsNullOrEmpty(extensionsFriendly))
        {
            extensionsFriendly = string.Join(", ", extensions);
        }

        // Include settings that affect LocalFileStorageService behaviour in the cache key.
        var cacheKey =
            $"{tenant.Id:N}|{baseDirectory}|{createIfMissing}|{allowOverwrite}|{maxBytes}|{string.Join(',', extensions)}|{pattern}";

        return _cache.GetOrAdd(cacheKey, _ =>
        {
            logger.LogInformation(
                "Creating local disk file storage for tenant {TenantName} at {BaseDirectory} (Provider={Provider})",
                tenant.Name,
                baseDirectory,
                provider);

            var options = new FileStorageOptions
            {
                Provider = "Local",
                Local = new LocalFileStorageOptions
                {
                    BaseDirectory = baseDirectory,
                    CreateDirectoryIfNotExists = createIfMissing,
                    AllowOverwrite = allowOverwrite,
                    MaxFileSizeBytes = maxBytes,
                    AllowedExtensions = extensions,
                    AllowedFileNamePattern = pattern,
                    AllowedFileNamePatternFriendlyList = patternFriendly,
                    AllowedExtensionsFriendlyList = extensionsFriendly
                }
            };

            return new LocalFileStorageService(options);
        });
    }

    private TenantConfiguration GetCurrentTenant()
    {
        var requestServices = httpContextAccessor.HttpContext?.RequestServices
            ?? throw new InvalidOperationException("No HttpContext available for tenant disk file storage.");

        var tenantContextAccessor = requestServices.GetRequiredService<ITenantContextAccessor>();
        return tenantContextAccessor.CurrentTenant
            ?? throw new InvalidOperationException("No tenant context available for disk file storage.");
    }
}
