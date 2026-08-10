namespace GovUK.Dfe.FlexForms.Domain.Tenancy;

/// <summary>
/// Clones TenantConfig for a new tenant (settings, one hostname, one frontend origin).
/// Does not copy principals, form templates, or other EA application data.
/// </summary>
public interface ITenantDuplicator
{
    /// <summary>
    /// Duplicates <paramref name="sourceTenantId"/> into a new tenant.
    /// Throws <see cref="KeyNotFoundException"/> when the source is missing,
    /// <see cref="InvalidOperationException"/> for uniqueness / validation conflicts.
    /// </summary>
    Task<DuplicateTenantResult> DuplicateAsync(
        Guid sourceTenantId,
        Guid newTenantId,
        string newTenantName,
        string hostname,
        string frontendOrigin,
        string authorizationApiSecretKey,
        string internalServiceAuthSecretKey,
        IReadOnlyList<(string Email, string ApiKey)> internalServiceAuthServiceApiKeys,
        string serviceName,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of a tenant duplication.
/// </summary>
public sealed record DuplicateTenantResult(
    Guid SourceTenantId,
    Guid NewTenantId,
    string NewTenantName,
    string Hostname,
    string FrontendOrigin,
    int SettingsCopied);
