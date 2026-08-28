using GovUK.Dfe.CoreLibs.FileStorage.Interfaces;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Builds Azure File Share clients from the current tenant's <c>FileStorage:Azure</c> settings
/// (not host GlobalConfiguration).
/// </summary>
public interface ITenantAzureFileStorageFactory
{
    /// <summary>
    /// Azure file storage for the current tenant when Provider is Azure or Hybrid; otherwise null.
    /// </summary>
    IFileStorageService? GetAzureFileStorageOrNull();

    /// <summary>
    /// Azure SAS operations for the current tenant when Provider is Azure or Hybrid; otherwise null.
    /// </summary>
    IAzureSpecificOperations? GetAzureOperationsOrNull();

    /// <summary>
    /// Required Azure file storage for the current tenant (Provider must be Azure).
    /// </summary>
    IFileStorageService GetRequiredAzureFileStorage();
}
