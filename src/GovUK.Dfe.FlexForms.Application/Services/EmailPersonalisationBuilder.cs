using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Builds Notify personalisation from hardcoded baseline keys plus optional
/// <c>EmailPlaceholderMappings</c> overlays from TenantConfig.
/// </summary>
public sealed class EmailPersonalisationBuilder(
    IEmailPlaceholderMappingProvider mappingProvider,
    IFieldMappingValueExtractor valueExtractor,
    ILogger<EmailPersonalisationBuilder> logger) : IEmailPersonalisationBuilder
{
    /// <inheritdoc />
    public async Task<Dictionary<string, object>> BuildAsync(
        string templateId,
        string emailType,
        Guid applicationId,
        string applicationReference,
        Dictionary<string, object> baseline,
        Dictionary<string, object> formData,
        IReadOnlyDictionary<string, object?>? platformMetadata = null,
        CancellationToken cancellationToken = default)
    {
        var personalization = new Dictionary<string, object>(baseline, StringComparer.OrdinalIgnoreCase);

        var mapping = await mappingProvider.GetMappingAsync(templateId, emailType, cancellationToken);
        if (mapping is null)
        {
            logger.LogDebug(
                "No EmailPlaceholderMappings for template {TemplateId} and email type {EmailType}; using baseline personalisation only.",
                templateId,
                emailType);
            return personalization;
        }

        logger.LogInformation(
            "Applying email placeholder mapping {MappingId} for template {TemplateId} and email type {EmailType}",
            mapping.MappingId,
            templateId,
            emailType);

        foreach (var (placeholderName, fieldMapping) in mapping.FieldMappings)
        {
            try
            {
                var value = valueExtractor.ExtractValue(
                    fieldMapping,
                    formData,
                    applicationId,
                    applicationReference,
                    platformMetadata);

                if (value == null || (value is string str && string.IsNullOrEmpty(str)))
                {
                    logger.LogTrace("Skipping email placeholder {PlaceholderName} - null or empty value", placeholderName);
                    continue;
                }

                // Notify personalisation values must be scalars (string/number/bool).
                personalization[placeholderName] = value is string or bool or byte or sbyte
                    or short or ushort or int or uint or long or ulong or float or double or decimal
                    ? value
                    : value.ToString() ?? string.Empty;

                logger.LogTrace("Mapped email placeholder {PlaceholderName} = {Value}", placeholderName, personalization[placeholderName]);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error extracting email placeholder {PlaceholderName} for email type {EmailType}",
                    placeholderName,
                    emailType);
                throw;
            }
        }

        return personalization;
    }
}
