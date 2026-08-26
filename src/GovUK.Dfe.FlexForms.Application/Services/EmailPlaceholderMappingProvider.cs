using System.Text.Json;
using GovUK.Dfe.FlexForms.Application.Common;
using GovUK.Dfe.FlexForms.Domain.Models.EventMapping;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Provides email placeholder mapping configurations from the current tenant's settings.
/// Shape under category <c>EmailPlaceholderMappings</c>: <c>{templateId}:{emailType}</c> nested objects.
/// </summary>
public sealed class EmailPlaceholderMappingProvider(
    ITenantContextAccessor tenantContextAccessor,
    ILogger<EmailPlaceholderMappingProvider> logger) : IEmailPlaceholderMappingProvider
{
    public const string SectionName = "EmailPlaceholderMappings";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <inheritdoc />
    public Task<EventFieldMapping?> GetMappingAsync(
        string templateId,
        string emailType,
        CancellationToken cancellationToken = default)
    {
        var settings = tenantContextAccessor.CurrentTenant?.Settings;
        if (settings is null)
        {
            logger.LogWarning(
                "No tenant context available when resolving email placeholder mapping for template {TemplateId} and email type {EmailType}.",
                templateId,
                emailType);
            return Task.FromResult<EventFieldMapping?>(null);
        }

        var exact = TryGetFromSection(settings.GetSection($"{SectionName}:{templateId}:{emailType}"), templateId, emailType);
        if (exact is not null)
        {
            logger.LogInformation(
                "Loaded email placeholder mapping from TenantConfig for template {TemplateId} and email type {EmailType} (MappingId: {MappingId})",
                templateId,
                emailType,
                exact.MappingId);
            return Task.FromResult<EventFieldMapping?>(exact);
        }

        // Admin UI often saves under the API template GUID, while the form schema may use a
        // legacy TemplateId (e.g. form-001). Search sibling keys for the same email type.
        return Task.FromResult(TryGetFromAnyTemplateKey(settings, emailType, templateId));
    }

    private EventFieldMapping? TryGetFromAnyTemplateKey(
        IConfiguration settings,
        string emailType,
        string preferredTemplateId)
    {
        var root = settings.GetSection(SectionName);
        if (!root.Exists())
            return null;

        var matches = new List<(string TemplateKey, EventFieldMapping Mapping)>();

        foreach (var templateSection in root.GetChildren())
        {
            if (string.Equals(templateSection.Key, "BasePath", StringComparison.OrdinalIgnoreCase))
                continue;

            var mapping = TryGetFromSection(templateSection.GetSection(emailType), templateSection.Key, emailType);
            if (mapping is null)
                continue;

            matches.Add((templateSection.Key, mapping));
        }

        if (matches.Count == 0)
        {
            logger.LogDebug(
                "No EmailPlaceholderMappings entry found for email type {EmailType} (requested template {TemplateId}).",
                emailType,
                preferredTemplateId);
            return null;
        }

        var preferredIsGuid = Guid.TryParse(preferredTemplateId, out _);
        var preferredMatch = matches.FirstOrDefault(m =>
            Guid.TryParse(m.TemplateKey, out _) != preferredIsGuid
            || string.Equals(m.TemplateKey, preferredTemplateId, StringComparison.OrdinalIgnoreCase));

        var chosen = preferredMatch.Mapping is not null ? preferredMatch : matches[0];

        if (matches.Count > 1)
        {
            logger.LogWarning(
                "Multiple EmailPlaceholderMappings entries found for email type {EmailType} under templates [{Keys}]. Using template key {ChosenKey}.",
                emailType,
                string.Join(", ", matches.Select(m => m.TemplateKey)),
                chosen.TemplateKey);
        }
        else
        {
            logger.LogInformation(
                "Resolved email placeholder mapping for {EmailType} under TenantConfig template key {TemplateKey} (requested {RequestedTemplateId})",
                emailType,
                chosen.TemplateKey,
                preferredTemplateId);
        }

        return chosen.Mapping;
    }

    private EventFieldMapping? TryGetFromSection(IConfigurationSection section, string templateId, string emailType)
    {
        if (!section.Exists() || (!section.GetChildren().Any() && string.IsNullOrEmpty(section.Value)))
            return null;

        var json = ConfigurationSectionJson.ToJson(section);
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var mapping = JsonSerializer.Deserialize<EventFieldMapping>(json, SerializerOptions);
            if (mapping is null)
            {
                logger.LogWarning(
                    "EmailPlaceholderMappings config for template {TemplateId} and email type {EmailType} deserialized to null. Raw JSON: {Json}",
                    templateId,
                    emailType,
                    json);
            }

            return mapping;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                ex,
                "Failed to deserialize EmailPlaceholderMappings config for template {TemplateId} and email type {EmailType}. Raw JSON: {Json}",
                templateId,
                emailType,
                json);
            return null;
        }
    }
}
