using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Invalidates cached permission and application listing data for a user.
/// </summary>
public interface IUserCacheInvalidator
{
    /// <summary>
    /// Removes cached permission claims, permission queries, and application listings for the specified user.
    /// </summary>
    /// <param name="email">The user's email address.</param>
    /// <param name="externalProviderId">The user's external provider identifier, when available.</param>
    /// <param name="userId">The user's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InvalidateForUserAsync(
        string? email,
        string? externalProviderId,
        UserId userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all tenant-scoped user permission claim caches (e.g. after role permission changes).
    /// </summary>
    Task InvalidateTenantUserClaimsAsync(CancellationToken cancellationToken = default);
}
