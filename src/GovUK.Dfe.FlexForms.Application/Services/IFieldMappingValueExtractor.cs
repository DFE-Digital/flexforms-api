using GovUK.Dfe.FlexForms.Domain.Models.EventMapping;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Extracts a single value from form data / metadata using a <see cref="FieldMapping"/> rule.
/// Shared by event payloads and email personalisation.
/// </summary>
public interface IFieldMappingValueExtractor
{
    /// <summary>
    /// Extracts a value based on the field mapping configuration.
    /// </summary>
    object? ExtractValue(
        FieldMapping fieldMapping,
        Dictionary<string, object> formData,
        Guid applicationId,
        string applicationReference,
        IReadOnlyDictionary<string, object?>? platformMetadata);
}
