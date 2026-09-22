namespace GovUK.Dfe.FlexForms.Domain.Tenancy;

/// <summary>
/// Reads raw TenantConfig setting rows (with secrets decrypted in-process).
/// Do not return decrypted secret JSON from HTTP handlers — redact first.
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
