using GovUK.Dfe.FlexForms.Domain.Tenancy;

namespace GovUK.Dfe.FlexForms.Domain.Services;

/// <summary>
/// Binds an external ID token's audience to a SaaS tenant's configured OIDC client ids.
/// Prevents exchanging a Transfers ID token under an LSRP <c>X-Tenant-ID</c> header.
/// </summary>
public interface ITenantOidcAudienceBinder
{
    /// <summary>
    /// Returns true when <paramref name="tokenAudiences"/> matches at least one OIDC client id
    /// configured for <paramref name="tenant"/> (DfESignIn / Entra SSO).
    /// When the tenant has no OIDC client ids configured (local/test), returns true.
    /// </summary>
    bool TokenMatchesTenant(TenantConfiguration tenant, IEnumerable<string> tokenAudiences);

    /// <summary>
    /// Collects configured OIDC client ids / audiences for the tenant.
    /// </summary>
    IReadOnlyCollection<string> GetConfiguredAudiences(TenantConfiguration tenant);
}
