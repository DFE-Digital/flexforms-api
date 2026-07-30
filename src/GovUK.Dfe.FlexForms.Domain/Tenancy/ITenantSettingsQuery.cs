namespace GovUK.Dfe.FlexForms.Domain.Tenancy;

/// <summary>
/// Reads raw TenantConfig setting rows (with secrets decrypted) for admin editing.
/// </summary>
public interface ITenantSettingsQuery
{
    /// <summary>
    /// Lists all setting categories for a tenant, decrypting secret values.
    /// Returns null when the tenant does not exist.
    /// </summary>
    Task<TenantSettingsList?> ListSettingsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
