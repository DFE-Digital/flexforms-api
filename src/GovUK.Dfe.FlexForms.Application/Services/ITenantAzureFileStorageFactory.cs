using GovUK.Dfe.CoreLibs.FileStorage.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Tenancy;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Builds Azure File Share clients from a tenant's <c>FileStorage:Azure</c> settings
/// (not host GlobalConfiguration). Singleton-safe: callers pass the tenant; this type
/// does not consume scoped <see cref="ITenantContextAccessor"/>.
/// </summary>
public interface ITenantAzureFileStorageFactory
{
    /// <summary>
    /// Azure file storage when Provider is Azure or Hybrid; otherwise null.
    /// </summary>
    IFileStorageService? GetAzureFileStorageOrNull(TenantConfiguration tenant);

    /// <summary>
    /// Azure SAS operations when Provider is Azure or Hybrid; otherwise null.
    /// </summary>
    IAzureSpecificOperations? GetAzureOperationsOrNull(TenantConfiguration tenant);

    /// <summary>
    /// Required Azure file storage (Provider must be Azure).
    /// </summary>
    IFileStorageService GetRequiredAzureFileStorage(TenantConfiguration tenant);
}
