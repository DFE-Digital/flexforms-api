namespace GovUK.Dfe.FlexForms.Application.Common.Models;

/// <summary>
/// Configuration for email templates organized by application / product type key.
/// Keys are tenant-defined (not platform service names).
/// </summary>
public class EmailTemplatesConfiguration : Dictionary<string, Dictionary<string, string>>
{
    /// <summary>
    /// Gets an email template ID for a specific application type and email type (case-insensitive keys).
    /// </summary>
    /// <param name="applicationType">Configured product key under EmailTemplates</param>
    /// <param name="emailType">The email type (e.g., "ApplicationSubmitted")</param>
    /// <returns>The template ID if found, otherwise null</returns>
    public string? GetTemplateId(string applicationType, string emailType)
    {
        var typeKey = FindApplicationTypeKey(applicationType);
        if (typeKey is null)
            return null;

        var typeTemplates = this[typeKey];
        foreach (var (key, templateId) in typeTemplates)
        {
            if (string.Equals(key, emailType, StringComparison.OrdinalIgnoreCase))
                return templateId;
        }

        return null;
    }

    /// <summary>
    /// Finds the configured EmailTemplates product key matching <paramref name="applicationType"/> (case-insensitive).
    /// </summary>
    public string? FindApplicationTypeKey(string? applicationType)
    {
        if (string.IsNullOrWhiteSpace(applicationType))
            return null;

        return Keys.FirstOrDefault(k =>
            string.Equals(k, applicationType, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets all available email types for a specific application type
    /// </summary>
    /// <param name="applicationType">The application type</param>
    /// <returns>Collection of available email types</returns>
    public IEnumerable<string> GetAvailableEmailTypes(string applicationType)
    {
        var typeKey = FindApplicationTypeKey(applicationType);
        return typeKey is not null
            ? this[typeKey].Keys
            : Enumerable.Empty<string>();
    }
}
