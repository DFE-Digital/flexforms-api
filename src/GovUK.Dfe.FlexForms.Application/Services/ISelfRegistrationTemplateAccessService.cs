using GovUK.Dfe.FlexForms.Domain.Entities;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Ensures self-registered users receive Template R/W only for the forms auto-registration allows:
/// the single live tenant form, or the configured defaults that are live when several forms are live.
/// </summary>
public interface ISelfRegistrationTemplateAccessService
{
    /// <summary>
    /// Grants Template R/W for auto-registration templates the user does not already have.
    /// Zero live forms, or several live forms with no live defaults, means no grant.
    /// Returns true when any grant was applied (caller should commit and invalidate caches).
    /// </summary>
    Task<bool> EnsureLiveTemplateAccessAsync(User user, CancellationToken cancellationToken = default);
}
