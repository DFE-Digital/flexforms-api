using GovUK.Dfe.CoreLibs.FileStorage.Interfaces;
using GovUK.Dfe.CoreLibs.FileStorage.Settings;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.Services
{
    /// <summary>
    /// Tenant-aware file storage service that resolves file storage settings
    /// from the current tenant's configuration at runtime.
    ///
    /// Host <c>GlobalConfiguration:FileStorage</c> registers the CoreLibs provider at startup.
    /// The tenant <c>FileStorage:Provider</c> must match that host registration:
    /// <list type="bullet">
    /// <item><c>Local</c> — files on disk; tenant Local options override BaseDirectory etc.</item>
    /// <item><c>Azure</c> — files on Azure File Share (host must be Provider=Azure).</item>
    /// <item><c>Hybrid</c> — CoreLibs writes to <b>local disk</b> and uses Azure only for SAS tokens
    /// (not for upload/download). Prefer <c>Azure</c> on App Service / containers.</item>
    /// </list>
    /// </summary>
    public class TenantAwareFileStorageService : ITenantAwareFileStorageService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<TenantAwareFileStorageService> _logger;

        public TenantAwareFileStorageService(
            IHttpContextAccessor httpContextAccessor,
            ILogger<TenantAwareFileStorageService> logger)
        {
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;

            _logger.LogDebug("TenantAwareFileStorageService initialized with lazy resolution support");
        }

        #region Default Interface Methods (without options override)

        public Task UploadAsync(string path, Stream content, string? originalFileName = null, CancellationToken cancellationToken = default)
        {
            var innerService = GetInnerService();
            var (provider, localOptions) = ResolveRequiredTenantStorage();
            EnsureHostMatchesTenantProvider(provider, innerService);

            if (localOptions != null)
            {
                _logger.LogDebug(
                    "Uploading file with tenant Local options (Provider={Provider}): path={Path}, baseDirectory={BaseDirectory}",
                    provider, path, localOptions.BaseDirectory);
                return innerService.UploadAsync(path, content, originalFileName, localOptions, cancellationToken);
            }

            _logger.LogDebug("Uploading file with Azure host registration (Provider={Provider}): path={Path}", provider, path);
            return innerService.UploadAsync(path, content, originalFileName, cancellationToken);
        }

        public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            var innerService = GetInnerService();
            var (provider, localOptions) = ResolveRequiredTenantStorage();
            EnsureHostMatchesTenantProvider(provider, innerService);

            if (localOptions != null)
            {
                return innerService.DeleteAsync(path, localOptions, cancellationToken);
            }

            return innerService.DeleteAsync(path, cancellationToken);
        }

        public Task<Stream> DownloadAsync(string path, CancellationToken cancellationToken = default)
        {
            var innerService = GetInnerService();
            var (provider, localOptions) = ResolveRequiredTenantStorage();
            EnsureHostMatchesTenantProvider(provider, innerService);

            if (localOptions != null)
            {
                return innerService.DownloadAsync(path, localOptions, cancellationToken);
            }

            return innerService.DownloadAsync(path, cancellationToken);
        }

        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            var innerService = GetInnerService();
            var (provider, localOptions) = ResolveRequiredTenantStorage();
            EnsureHostMatchesTenantProvider(provider, innerService);

            if (localOptions != null)
            {
                return innerService.ExistsAsync(path, localOptions, cancellationToken);
            }

            return innerService.ExistsAsync(path, cancellationToken);
        }

        #endregion

        #region Interface Methods with Options Override

        public Task UploadAsync(string path, Stream content, string? originalFileName, LocalFileStorageOptions? optionsOverride, CancellationToken cancellationToken = default)
        {
            var innerService = GetInnerService();

            if (optionsOverride != null)
            {
                _logger.LogDebug("Uploading file with explicit options override: path={Path}, baseDirectory={BaseDirectory}",
                    path, optionsOverride.BaseDirectory);
                return innerService.UploadAsync(path, content, originalFileName, optionsOverride, cancellationToken);
            }

            return UploadAsync(path, content, originalFileName, cancellationToken);
        }

        public Task DeleteAsync(string path, LocalFileStorageOptions? optionsOverride, CancellationToken cancellationToken = default)
        {
            var innerService = GetInnerService();

            if (optionsOverride != null)
            {
                return innerService.DeleteAsync(path, optionsOverride, cancellationToken);
            }

            return DeleteAsync(path, cancellationToken);
        }

        public Task<Stream> DownloadAsync(string path, LocalFileStorageOptions? optionsOverride, CancellationToken cancellationToken = default)
        {
            var innerService = GetInnerService();

            if (optionsOverride != null)
            {
                return innerService.DownloadAsync(path, optionsOverride, cancellationToken);
            }

            return DownloadAsync(path, cancellationToken);
        }

        public Task<bool> ExistsAsync(string path, LocalFileStorageOptions? optionsOverride, CancellationToken cancellationToken = default)
        {
            var innerService = GetInnerService();

            if (optionsOverride != null)
            {
                return innerService.ExistsAsync(path, optionsOverride, cancellationToken);
            }

            return ExistsAsync(path, cancellationToken);
        }

        #endregion

        #region Private Helpers

        private IFileStorageService GetInnerService()
        {
            var requestServices = _httpContextAccessor.HttpContext?.RequestServices
                ?? throw new InvalidOperationException("No HttpContext available for file storage operation");

            return requestServices.GetRequiredService<IFileStorageService>();
        }

        /// <summary>
        /// Returns tenant provider and Local options when files are stored on disk (Local or Hybrid).
        /// Azure returns null local options (CoreLibs has no Azure options override).
        /// </summary>
        private (string Provider, LocalFileStorageOptions? LocalOptions) ResolveRequiredTenantStorage()
        {
            var requestServices = _httpContextAccessor.HttpContext?.RequestServices
                ?? throw new InvalidOperationException("No HttpContext available for file storage operation");

            var tenantContextAccessor = requestServices.GetService<ITenantContextAccessor>()
                ?? throw new InvalidOperationException("ITenantContextAccessor is not available for file storage operation");

            var tenant = tenantContextAccessor.CurrentTenant
                ?? throw new InvalidOperationException("No tenant context available for file storage operation");

            var provider = tenant.Settings.GetValue<string>("FileStorage:Provider");
            if (string.IsNullOrWhiteSpace(provider))
            {
                throw new InvalidOperationException(
                    $"Tenant '{tenant.Name}' has no FileStorage:Provider configured. " +
                    "File operations require a tenant FileStorage setting and do not fall back to host GlobalConfiguration.");
            }

            if (string.Equals(provider, "Local", StringComparison.OrdinalIgnoreCase)
                || string.Equals(provider, "Hybrid", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(provider, "Hybrid", StringComparison.OrdinalIgnoreCase))
                {
                    EnsureAzureSectionPresent(tenant, provider);
                }

                var baseDirectory = tenant.Settings.GetValue<string>("FileStorage:Local:BaseDirectory");
                if (string.IsNullOrEmpty(baseDirectory))
                {
                    throw new InvalidOperationException(
                        $"Tenant '{tenant.Name}' FileStorage Provider is {provider} but FileStorage:Local:BaseDirectory is missing. " +
                        (string.Equals(provider, "Hybrid", StringComparison.OrdinalIgnoreCase)
                            ? "Hybrid stores files on local disk (Azure is used only for SAS). For Azure File Share uploads use Provider=Azure."
                            : string.Empty));
                }

                var options = BuildLocalOptions(tenant, baseDirectory);
                _logger.LogDebug(
                    "Resolved tenant disk options for {TenantName} Provider={Provider}: BaseDirectory={BaseDirectory}",
                    tenant.Name, provider, options.BaseDirectory);
                return (provider, options);
            }

            if (string.Equals(provider, "Azure", StringComparison.OrdinalIgnoreCase))
            {
                EnsureAzureSectionPresent(tenant, provider);
                return (provider, null);
            }

            throw new InvalidOperationException(
                $"Tenant '{tenant.Name}' has unsupported FileStorage:Provider '{provider}'. Expected Local, Azure, or Hybrid.");
        }

        private static void EnsureAzureSectionPresent(TenantConfiguration tenant, string provider)
        {
            var connectionString = tenant.Settings.GetValue<string>("FileStorage:Azure:ConnectionString");
            var shareName = tenant.Settings.GetValue<string>("FileStorage:Azure:ShareName");
            if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(shareName))
            {
                throw new InvalidOperationException(
                    $"Tenant '{tenant.Name}' FileStorage Provider is {provider} but FileStorage:Azure:ConnectionString and/or ShareName is missing.");
            }
        }

        private static LocalFileStorageOptions BuildLocalOptions(TenantConfiguration tenant, string baseDirectory)
        {
            var options = new LocalFileStorageOptions
            {
                BaseDirectory = baseDirectory,
                CreateDirectoryIfNotExists = tenant.Settings.GetValue<bool?>("FileStorage:Local:CreateDirectoryIfNotExists") ?? true,
                AllowOverwrite = tenant.Settings.GetValue<bool?>("FileStorage:Local:AllowOverwrite") ?? true,
                MaxFileSizeBytes = tenant.Settings.GetValue<long?>("FileStorage:Local:MaxFileSizeBytes") ?? 100 * 1024 * 1024,
                AllowedExtensions = tenant.Settings.GetSection("FileStorage:Local:AllowedExtensions").Get<string[]>() ?? Array.Empty<string>(),
                AllowedFileNamePattern = tenant.Settings.GetValue<string>("FileStorage:Local:AllowedFileNamePattern"),
                AllowedFileNamePatternFriendlyList = tenant.Settings.GetValue<string>("FileStorage:Local:AllowedFileNamePatternFriendlyList") ?? "a-z A-Z 0-9 _ - no-space",
                AllowedExtensionsFriendlyList = tenant.Settings.GetValue<string>("FileStorage:Local:AllowedExtensionsFriendlyList") ?? string.Empty
            };
            if (string.IsNullOrEmpty(options.AllowedExtensionsFriendlyList))
            {
                options.AllowedExtensionsFriendlyList = string.Join(", ", options.AllowedExtensions);
            }

            return options;
        }

        /// <summary>
        /// Host DI picks one CoreLibs implementation at startup. Tenant Provider must match that
        /// implementation or uploads silently hit the wrong store (e.g. Local /app/uploads).
        /// Unknown types (test doubles) are skipped.
        /// </summary>
        private static void EnsureHostMatchesTenantProvider(string tenantProvider, IFileStorageService inner)
        {
            var hostImpl = inner.GetType().Name;
            if (!KnownCoreLibsImplementations.Contains(hostImpl))
            {
                return;
            }

            if (string.Equals(tenantProvider, "Azure", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(hostImpl, "AzureFileStorageService", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Tenant FileStorage Provider is Azure but the API host registered '{hostImpl}'. " +
                        "Set GlobalConfiguration:FileStorage:Provider=Azure with Azure:ConnectionString and ShareName on the API host. " +
                        "Provider=Hybrid stores files on local disk and only uses Azure for SAS tokens — use Provider=Azure for Azure File Share.");
                }

                return;
            }

            if (string.Equals(tenantProvider, "Hybrid", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(hostImpl, "HybridFileStorageService", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Tenant FileStorage Provider is Hybrid but the API host registered '{hostImpl}'. " +
                        "Set GlobalConfiguration:FileStorage:Provider=Hybrid (Local + Azure sections) on the API host, " +
                        "or change the tenant Provider to match the host (Azure for File Share, Local for disk). " +
                        "Hybrid always writes files to local disk; Azure is only used for SAS.");
                }

                return;
            }

            if (string.Equals(tenantProvider, "Local", StringComparison.OrdinalIgnoreCase))
            {
                // Local tenant can use Local or Hybrid host (both support Local options override).
                if (!string.Equals(hostImpl, "LocalFileStorageService", StringComparison.Ordinal)
                    && !string.Equals(hostImpl, "HybridFileStorageService", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Tenant FileStorage Provider is Local but the API host registered '{hostImpl}'. " +
                        "Set GlobalConfiguration:FileStorage:Provider=Local (or Hybrid) on the API host.");
                }
            }
        }

        private static readonly HashSet<string> KnownCoreLibsImplementations = new(StringComparer.Ordinal)
        {
            "LocalFileStorageService",
            "AzureFileStorageService",
            "HybridFileStorageService"
        };

        #endregion
    }
}
