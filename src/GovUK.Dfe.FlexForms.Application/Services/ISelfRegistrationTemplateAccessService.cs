using GovUK.Dfe.FlexForms.Domain.Entities;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Ensures self-registered users receive Template R/W for all live forms in the current tenant.
/// </summary>
public interface ISelfRegistrationTemplateAccessService
{
    /// <summary>
    /// Grants Template R/W for each live tenant template the user does not already have.
    /// Returns true when any grant was applied (caller should commit and invalidate caches).
    /// </summary>
    Task<bool> EnsureLiveTemplateAccessAsync(User user, CancellationToken cancellationToken = default);
}
