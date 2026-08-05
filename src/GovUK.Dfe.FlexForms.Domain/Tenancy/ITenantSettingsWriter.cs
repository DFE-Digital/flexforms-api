namespace GovUK.Dfe.FlexForms.Domain.Tenancy;

/// <summary>
/// Persists tenant setting categories to the tenant configuration store.
/// Handles encryption of secret categories transparently.
/// </summary>
public interface ITenantSettingsWriter
{
    /// <summary>
    /// Inserts or updates a setting category for a tenant.
    /// If a row with the same (TenantId, Category, Target) exists it is updated; otherwise a new row is inserted.
    /// Secret settings are encrypted before storage.
    /// </summary>
    Task<UpsertTenantSettingResult> UpsertSettingAsync(
        Guid tenantId,
        string category,
        string target,
        string settingsJson,
        bool isSecret,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a setting category for a tenant. Returns null when the row does not exist.
    /// </summary>
    Task<DeleteTenantSettingResult?> DeleteSettingAsync(
        Guid tenantId,
        string category,
        string target,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of an upsert operation on a tenant setting.
/// </summary>
public sealed record UpsertTenantSettingResult(
    Guid SettingId,
    bool WasCreated,
    string Category,
    string Target);

/// <summary>
/// Result of deleting a tenant setting.
/// </summary>
public sealed record DeleteTenantSettingResult(
    Guid SettingId,
    string Category,
    string Target,
    bool WasSecret);
