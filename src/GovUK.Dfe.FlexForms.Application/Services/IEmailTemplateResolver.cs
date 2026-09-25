using GovUK.Dfe.FlexForms.Domain.ValueObjects;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Service for resolving email template IDs based on application template and email type
/// </summary>
public interface IEmailTemplateResolver
{
    /// <summary>
    /// Resolves the email template ID for a given application template and email type
    /// </summary>
    /// <param name="templateId">The template ID from the application</param>
    /// <param name="emailType">The type of email (e.g., "ApplicationSubmitted")</param>
    /// <returns>The email template ID if found, otherwise null</returns>
    Task<string?> ResolveEmailTemplateAsync(TemplateId templateId, string emailType);

    /// <summary>
    /// Resolves a tenant-wide email template ID that is not tied to a specific form template
    /// (e.g. the Test Authentication one-time password email). Prefers the <c>EmailTemplates</c>
    /// product key matching the tenant name, then the first product key that defines the email type.
    /// </summary>
    /// <param name="emailType">The type of email (e.g., "TestAuthPasswordEmail")</param>
    /// <returns>The email template ID if found, otherwise null</returns>
    Task<string?> ResolveTenantEmailTemplateAsync(string emailType);

    /// <summary>
    /// Gets the application type name for a given template ID
    /// </summary>
    /// <param name="templateId">The template ID</param>
    /// <returns>The application type name if found, otherwise null</returns>
    Task<string?> GetApplicationTypeAsync(TemplateId templateId);
}
