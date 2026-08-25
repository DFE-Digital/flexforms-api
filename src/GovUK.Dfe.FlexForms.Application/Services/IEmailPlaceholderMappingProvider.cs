using GovUK.Dfe.FlexForms.Domain.Models.EventMapping;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Provides email placeholder mapping configurations for the current tenant.
/// </summary>
public interface IEmailPlaceholderMappingProvider
{
    /// <summary>
    /// Gets the email placeholder mapping for a form template and email type.
    /// </summary>
    /// <param name="templateId">The form template ID (API GUID or schema-embedded id such as form-001)</param>
    /// <param name="emailType">The email type (e.g. ApplicationSubmitted, ContributorInvited)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The mapping configuration, or null if not found</returns>
    Task<EventFieldMapping?> GetMappingAsync(
        string templateId,
        string emailType,
        CancellationToken cancellationToken = default);
}
