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
    /// Removes all tenant-scoped user permission caches (claims, GetMyPermissions queries, template permissions).
    /// Call after role permission changes or other bulk permission updates.
    /// </summary>
    Task InvalidateTenantUserClaimsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every tenant-scoped application listing cache (by template, by user email, and by external id).
    /// Call after an application status change so View applications shows the current status.
    /// </summary>
    Task InvalidateApplicationListingsAsync(CancellationToken cancellationToken = default);
}
