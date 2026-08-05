namespace GovUK.Dfe.FlexForms.Domain.Tenancy;

/// <summary>
/// Resolves a tenant from an incoming HTTP hostname.
/// </summary>
public interface ITenantHostnameResolver
{
    /// <summary>
    /// Looks up an active tenant by hostname (case-insensitive).
    /// </summary>
    Task<TenantHostnameResolution?> ResolveAsync(string hostname, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists hostnames registered for a tenant.
    /// </summary>
    Task<IReadOnlyList<string>> ListHostnamesForTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of resolving a hostname to a tenant.
/// </summary>
public sealed record TenantHostnameResolution(Guid TenantId, string TenantName, string Hostname);
