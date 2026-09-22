namespace GovUK.Dfe.FlexForms.Domain.Tenancy;

/// <summary>
/// A single TenantConfig settings row for admin APIs (secrets decrypted in-process).
/// Callers must redact via <see cref="TenantSettingSecretJson"/> before returning to a client.
/// </summary>
public sealed record TenantSettingRow(
    Guid SettingId,
    string Category,
    string Target,
    string SettingsJson,
    bool IsSecret,
    DateTime UpdatedAtUtc);

/// <summary>
/// Raw TenantConfig settings for a tenant.
/// </summary>
public sealed record TenantSettingsList(
    Guid TenantId,
    string TenantName,
    IReadOnlyCollection<TenantSettingRow> Settings);
