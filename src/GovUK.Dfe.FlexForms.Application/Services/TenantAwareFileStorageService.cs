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
    /// Tenant-aware file storage. Host <c>GlobalConfiguration:FileStorage</c> only boots CoreLibs DI
    /// (dummy Local path). Runtime behaviour comes from TenantConfig:
    /// <list type="bullet">
    /// <item>
    /// <c>Hybrid</c> (Container Apps with Azure Files mount) — write/read via a per-tenant
    /// <see cref="ITenantDiskFileStorageFactory"/> at <c>Local.BaseDirectory</c> (e.g. <c>/uploads</c>);
    /// SAS via tenant Azure credentials against the same share.
    /// </item>
    /// <item><c>Local</c> — disk only via <see cref="ITenantDiskFileStorageFactory"/>.</item>
    /// <item><c>Azure</c> — upload/download/delete/SAS via Azure SDK using tenant Azure settings (no mount).</item>
    /// </list>
    /// Host <see cref="IFileStorageService"/> is not used for Local/Hybrid uploads: CoreLibs
    /// <c>LocalFileStorageService</c> creates the host BaseDirectory in its constructor (e.g.
    /// <c>/app/uploads</c>), which fails in containers before any options override can run.
    /// </summary>
    public class TenantAwareFileStorageService : ITenantAwareFileStorageService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ITenantAzureFileStorageFactory _tenantAzureFactory;
        private readonly ITenantDiskFileStorageFactory _tenantDiskFactory;
        private readonly ILogger<TenantAwareFileStorageService> _logger;

        public TenantAwareFileStorageService(
            IHttpContextAccessor httpContextAccessor,
            ITenantAzureFileStorageFactory tenantAzureFactory,
            ITenantDiskFileStorageFactory tenantDiskFactory,
            ILogger<TenantAwareFileStorageService> logger)
        {
            _httpContextAccessor = httpContextAccessor;
            _tenantAzureFactory = tenantAzureFactory;
            _tenantDiskFactory = tenantDiskFactory;
            _logger = logger;
        }

        #region Default Interface Methods

        public Task UploadAsync(string path, Stream content, string? originalFileName = null, CancellationToken cancellationToken = default)
        {
            var (provider, localOptions) = ResolveRequiredTenantStorage();

            if (string.Equals(provider, "Azure", StringComparison.OrdinalIgnoreCase))
            {
                ValidateAgainstTenantLocalRules(originalFileName, content, localOptions);
                var azure = _tenantAzureFactory.GetRequiredAzureFileStorage();
                _logger.LogDebug("Uploading to tenant Azure File Share: path={Path}", path);
                return azure.UploadAsync(path, content, originalFileName, cancellationToken);
            }

            var disk = _tenantDiskFactory.GetRequiredDiskFileStorage();
            _logger.LogDebug(
                "Uploading to tenant disk (Provider={Provider}): path={Path}, baseDirectory={BaseDirectory}",
                provider, path, localOptions?.BaseDirectory);
            return disk.UploadAsync(path, content, originalFileName, cancellationToken);
        }

        public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            var (provider, _) = ResolveRequiredTenantStorage();

            if (string.Equals(provider, "Azure", StringComparison.OrdinalIgnoreCase))
            {
                return _tenantAzureFactory.GetRequiredAzureFileStorage().DeleteAsync(path, cancellationToken);
            }

            return _tenantDiskFactory.GetRequiredDiskFileStorage().DeleteAsync(path, cancellationToken);
        }

        public Task<Stream> DownloadAsync(string path, CancellationToken cancellationToken = default)
        {
            var (provider, _) = ResolveRequiredTenantStorage();

            if (string.Equals(provider, "Azure", StringComparison.OrdinalIgnoreCase))
            {
                return _tenantAzureFactory.GetRequiredAzureFileStorage().DownloadAsync(path, cancellationToken);
            }

            return _tenantDiskFactory.GetRequiredDiskFileStorage().DownloadAsync(path, cancellationToken);
        }

        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            var (provider, _) = ResolveRequiredTenantStorage();

            if (string.Equals(provider, "Azure", StringComparison.OrdinalIgnoreCase))
            {
                return _tenantAzureFactory.GetRequiredAzureFileStorage().ExistsAsync(path, cancellationToken);
            }

            return _tenantDiskFactory.GetRequiredDiskFileStorage().ExistsAsync(path, cancellationToken);
        }

        #endregion

        #region Options Override

        public Task UploadAsync(string path, Stream content, string? originalFileName, LocalFileStorageOptions? optionsOverride, CancellationToken cancellationToken = default)
        {
            if (optionsOverride != null)
            {
                // Explicit override still goes through host CoreLibs service (test/rare path).
                return GetInnerHostService().UploadAsync(path, content, originalFileName, optionsOverride, cancellationToken);
            }

            return UploadAsync(path, content, originalFileName, cancellationToken);
        }

        public Task DeleteAsync(string path, LocalFileStorageOptions? optionsOverride, CancellationToken cancellationToken = default)
        {
            if (optionsOverride != null)
            {
                return GetInnerHostService().DeleteAsync(path, optionsOverride, cancellationToken);
            }

            return DeleteAsync(path, cancellationToken);
        }

        public Task<Stream> DownloadAsync(string path, LocalFileStorageOptions? optionsOverride, CancellationToken cancellationToken = default)
        {
            if (optionsOverride != null)
            {
                return GetInnerHostService().DownloadAsync(path, optionsOverride, cancellationToken);
            }

            return DownloadAsync(path, cancellationToken);
        }

        public Task<bool> ExistsAsync(string path, LocalFileStorageOptions? optionsOverride, CancellationToken cancellationToken = default)
        {
            if (optionsOverride != null)
            {
                return GetInnerHostService().ExistsAsync(path, optionsOverride, cancellationToken);
            }

            return ExistsAsync(path, cancellationToken);
        }

        #endregion

        #region Private Helpers

        private IFileStorageService GetInnerHostService()
        {
            var requestServices = _httpContextAccessor.HttpContext?.RequestServices
                ?? throw new InvalidOperationException("No HttpContext available for file storage operation");

            return requestServices.GetRequiredService<IFileStorageService>();
        }

        /// <summary>
        /// For Azure, LocalOptions may still carry AllowedExtensions / MaxFileSize from the tenant Local section.
        /// For Local/Hybrid, LocalOptions is required for disk BaseDirectory.
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
                    "File operations require a tenant FileStorage setting.");
            }

            if (string.Equals(provider, "Azure", StringComparison.OrdinalIgnoreCase))
            {
                var optionalLocal = TryBuildLocalOptions(tenant);
                return (provider, optionalLocal);
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
                        $"Tenant '{tenant.Name}' FileStorage Provider is {provider} but FileStorage:Local:BaseDirectory is missing.");
                }

                return (provider, BuildLocalOptions(tenant, baseDirectory));
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

        private static LocalFileStorageOptions? TryBuildLocalOptions(TenantConfiguration tenant)
        {
            var baseDirectory = tenant.Settings.GetValue<string>("FileStorage:Local:BaseDirectory");
            var hasExtensions = tenant.Settings.GetSection("FileStorage:Local:AllowedExtensions").Exists();
            var hasMax = tenant.Settings.GetValue<long?>("FileStorage:Local:MaxFileSizeBytes").HasValue;
            if (string.IsNullOrEmpty(baseDirectory) && !hasExtensions && !hasMax)
            {
                return null;
            }

            return BuildLocalOptions(tenant, baseDirectory ?? string.Empty);
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
        /// CoreLibs Azure service does not enforce Local AllowedExtensions / MaxFileSize; apply tenant rules here.
        /// </summary>
        private static void ValidateAgainstTenantLocalRules(
            string? originalFileName,
            Stream content,
            LocalFileStorageOptions? rules)
        {
            if (rules is null)
            {
                return;
            }

            if (rules.AllowedExtensions is { Length: > 0 })
            {
                var extension = Path.GetExtension(originalFileName ?? string.Empty).TrimStart('.').ToLowerInvariant();
                if (string.IsNullOrEmpty(extension)
                    || !rules.AllowedExtensions.Any(e =>
                        string.Equals(e.TrimStart('.'), extension, StringComparison.OrdinalIgnoreCase)))
                {
                    var friendly = string.IsNullOrWhiteSpace(rules.AllowedExtensionsFriendlyList)
                        ? string.Join(", ", rules.AllowedExtensions)
                        : rules.AllowedExtensionsFriendlyList;
                    throw new InvalidOperationException($"File extension is not allowed. Allowed: {friendly}");
                }
            }

            if (rules.MaxFileSizeBytes > 0 && content.CanSeek && content.Length > rules.MaxFileSizeBytes)
            {
                throw new InvalidOperationException(
                    $"File size exceeds the maximum allowed size of {rules.MaxFileSizeBytes} bytes.");
            }
        }

        #endregion
    }
}
