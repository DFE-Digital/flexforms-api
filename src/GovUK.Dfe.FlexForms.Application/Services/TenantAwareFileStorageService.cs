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
    /// Host <c>GlobalConfiguration:FileStorage</c> is used only to register CoreLibs DI.
    /// Per-request operations require a complete tenant <c>FileStorage</c> setting and
    /// do not fall back to host defaults when that setting is missing or incomplete.
    ///
    /// Both the inner IFileStorageService and ITenantContextAccessor are resolved lazily
    /// via HttpContext.RequestServices to avoid DI lifetime issues (the CoreLibs service
    /// uses concrete type resolution internally that can't be decorated).
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
            var tenantOptions = ResolveRequiredTenantLocalOptions();

            if (tenantOptions != null)
            {
                _logger.LogDebug("Uploading file with tenant options: path={Path}, baseDirectory={BaseDirectory}",
                    path, tenantOptions.BaseDirectory);
                return innerService.UploadAsync(path, content, originalFileName, tenantOptions, cancellationToken);
            }

            // Azure/Hybrid: tenant FileStorage validated; CoreLibs has no Azure options override.
            _logger.LogDebug("Uploading file with tenant-validated Azure/Hybrid host registration: path={Path}", path);
            return innerService.UploadAsync(path, content, originalFileName, cancellationToken);
        }

        public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            var innerService = GetInnerService();
            var tenantOptions = ResolveRequiredTenantLocalOptions();

            if (tenantOptions != null)
            {
                _logger.LogDebug("Deleting file with tenant options: path={Path}, baseDirectory={BaseDirectory}",
                    path, tenantOptions.BaseDirectory);
                return innerService.DeleteAsync(path, tenantOptions, cancellationToken);
            }

            _logger.LogDebug("Deleting file with tenant-validated Azure/Hybrid host registration: path={Path}", path);
            return innerService.DeleteAsync(path, cancellationToken);
        }

        public Task<Stream> DownloadAsync(string path, CancellationToken cancellationToken = default)
        {
            var innerService = GetInnerService();
            var tenantOptions = ResolveRequiredTenantLocalOptions();

            if (tenantOptions != null)
            {
                _logger.LogDebug("Downloading file with tenant options: path={Path}, baseDirectory={BaseDirectory}",
                    path, tenantOptions.BaseDirectory);
                return innerService.DownloadAsync(path, tenantOptions, cancellationToken);
            }

            _logger.LogDebug("Downloading file with tenant-validated Azure/Hybrid host registration: path={Path}", path);
            return innerService.DownloadAsync(path, cancellationToken);
        }

        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            var innerService = GetInnerService();
            var tenantOptions = ResolveRequiredTenantLocalOptions();

            if (tenantOptions != null)
            {
                return innerService.ExistsAsync(path, tenantOptions, cancellationToken);
            }

            return innerService.ExistsAsync(path, cancellationToken);
        }

        #endregion

        #region Interface Methods with Options Override

        public Task UploadAsync(string path, Stream content, string? originalFileName, LocalFileStorageOptions? optionsOverride, CancellationToken cancellationToken = default)
        {
            var innerService = GetInnerService();

            // When explicit options are provided, use them directly (allows caller to override tenant options)
            if (optionsOverride != null)
            {
                _logger.LogDebug("Uploading file with explicit options override: path={Path}, baseDirectory={BaseDirectory}",
                    path, optionsOverride.BaseDirectory);
                return innerService.UploadAsync(path, content, originalFileName, optionsOverride, cancellationToken);
            }

            // If no override provided, use tenant options
            return UploadAsync(path, content, originalFileName, cancellationToken);
        }

        public Task DeleteAsync(string path, LocalFileStorageOptions? optionsOverride, CancellationToken cancellationToken = default)
        {
            var innerService = GetInnerService();

            if (optionsOverride != null)
            {
                _logger.LogDebug("Deleting file with explicit options override: path={Path}, baseDirectory={BaseDirectory}",
                    path, optionsOverride.BaseDirectory);
                return innerService.DeleteAsync(path, optionsOverride, cancellationToken);
            }

            return DeleteAsync(path, cancellationToken);
        }

        public Task<Stream> DownloadAsync(string path, LocalFileStorageOptions? optionsOverride, CancellationToken cancellationToken = default)
        {
            var innerService = GetInnerService();

            if (optionsOverride != null)
            {
                _logger.LogDebug("Downloading file with explicit options override: path={Path}, baseDirectory={BaseDirectory}",
                    path, optionsOverride.BaseDirectory);
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

        /// <summary>
        /// Gets the inner IFileStorageService from the current request's service provider.
        /// This lazy resolution avoids DI lifetime issues with CoreLibs' internal concrete type resolution.
        /// </summary>
        private IFileStorageService GetInnerService()
        {
            var requestServices = _httpContextAccessor.HttpContext?.RequestServices
                ?? throw new InvalidOperationException("No HttpContext available for file storage operation");

            return requestServices.GetRequiredService<IFileStorageService>();
        }

        /// <summary>
        /// Resolves Local options for the current tenant, or null when Provider is Azure/Hybrid
        /// (after validating required Azure settings). Throws when tenant FileStorage is missing
        /// or incomplete — never falls back to host GlobalConfiguration defaults.
        /// </summary>
        private LocalFileStorageOptions? ResolveRequiredTenantLocalOptions()
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

            if (string.Equals(provider, "Local", StringComparison.OrdinalIgnoreCase))
            {
                var baseDirectory = tenant.Settings.GetValue<string>("FileStorage:Local:BaseDirectory");
                if (string.IsNullOrEmpty(baseDirectory))
                {
                    throw new InvalidOperationException(
                        $"Tenant '{tenant.Name}' FileStorage Provider is Local but FileStorage:Local:BaseDirectory is missing.");
                }

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

                _logger.LogDebug("Resolved tenant Local options for {TenantName}: BaseDirectory={BaseDirectory}",
                    tenant.Name, options.BaseDirectory);

                return options;
            }

            if (string.Equals(provider, "Azure", StringComparison.OrdinalIgnoreCase)
                || string.Equals(provider, "Hybrid", StringComparison.OrdinalIgnoreCase))
            {
                var connectionString = tenant.Settings.GetValue<string>("FileStorage:Azure:ConnectionString");
                var shareName = tenant.Settings.GetValue<string>("FileStorage:Azure:ShareName");
                if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(shareName))
                {
                    throw new InvalidOperationException(
                        $"Tenant '{tenant.Name}' FileStorage Provider is {provider} but FileStorage:Azure:ConnectionString and/or ShareName is missing.");
                }

                _logger.LogDebug(
                    "Tenant {TenantName} FileStorage Provider={Provider} validated; using host-registered Azure/Hybrid service (no per-call Azure options override).",
                    tenant.Name, provider);
                return null;
            }

            throw new InvalidOperationException(
                $"Tenant '{tenant.Name}' has unsupported FileStorage:Provider '{provider}'. Expected Local, Azure, or Hybrid.");
        }

        #endregion
    }
}
