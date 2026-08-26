namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Builds GOV.UK Notify personalisation dictionaries from baseline keys plus optional
/// TenantConfig <c>EmailPlaceholderMappings</c> overlays.
/// </summary>
public interface IEmailPersonalisationBuilder
{
    /// <summary>
    /// Builds personalisation starting from <paramref name="baseline"/>, then overlays any
    /// configured field mappings for <paramref name="templateId"/> / <paramref name="emailType"/>.
    /// When no mapping exists, returns a copy of the baseline unchanged.
    /// </summary>
    Task<Dictionary<string, object>> BuildAsync(
        string templateId,
        string emailType,
        Guid applicationId,
        string applicationReference,
        Dictionary<string, object> baseline,
        Dictionary<string, object> formData,
        IReadOnlyDictionary<string, object?>? platformMetadata = null,
        CancellationToken cancellationToken = default);
}
