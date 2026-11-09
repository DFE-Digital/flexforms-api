namespace GovUK.Dfe.FlexForms.Domain.Tenancy;

/// <summary>
/// Creates a new TenantConfig tenant (one hostname, one frontend origin and the given settings rows).
/// Does not copy anything from another tenant, and does not create principals or EA data.
/// </summary>
public interface ITenantCreator
{
    /// <summary>
    /// Persists <paramref name="tenant"/>. Settings JSON must already be plaintext and final
    /// (no redaction sentinels); rows with <see cref="NewTenantSetting.IsSecret"/> are encrypted.
    /// Throws <see cref="InvalidOperationException"/> for validation / uniqueness conflicts and
    /// <see cref="TenantSettingTargetConflictException"/> when rows overlap Shared and Api/Web.
    /// </summary>
    Task<CreateTenantResult> CreateAsync(
        NewTenantDefinition tenant,
        CancellationToken cancellationToken = default);
}

public sealed record NewTenantDefinition(
    Guid TenantId,
    string Name,
    string Hostname,
    string FrontendOrigin,
    IReadOnlyList<NewTenantSetting> Settings);

public sealed record NewTenantSetting(
    string Category,
    string Target,
    string SettingsJson,
    bool IsSecret);

public sealed record CreateTenantResult(
    Guid TenantId,
    string Name,
    string Hostname,
    string FrontendOrigin,
    int SettingsCreated);
