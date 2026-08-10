using System.Text.Json;
using GovUK.Dfe.FlexForms.Application.Common;
using GovUK.Dfe.FlexForms.Domain.Models.EventMapping;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Provides event field mapping configurations from the current tenant's settings.
/// Shape under category <c>EventMappings</c>: <c>{templateId}:{eventType}</c> nested objects.
/// </summary>
public sealed class EventMappingProvider(
    ITenantContextAccessor tenantContextAccessor,
    ILogger<EventMappingProvider> logger) : IEventMappingProvider
{
    public const string SectionName = "EventMappings";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <inheritdoc />
    public Task<EventFieldMapping?> GetMappingAsync(
        string templateId,
        string eventType,
        CancellationToken cancellationToken = default)
    {
        var settings = tenantContextAccessor.CurrentTenant?.Settings;
        if (settings is null)
        {
            logger.LogWarning(
                "No tenant context available when resolving event mapping for template {TemplateId} and event {EventType}.",
                templateId,
                eventType);
            return Task.FromResult<EventFieldMapping?>(null);
        }

        var exact = TryGetFromSection(settings.GetSection($"{SectionName}:{templateId}:{eventType}"), templateId, eventType);
        if (exact is not null)
        {
            logger.LogInformation(
                "Loaded event mapping from TenantConfig for template {TemplateId} and event {EventType} (MappingId: {MappingId})",
                templateId,
                eventType,
                exact.MappingId);
            return Task.FromResult<EventFieldMapping?>(exact);
        }

        // Admin UI often saves under the API template GUID, while the form schema may use a
        // legacy TemplateId (e.g. form-001). Search sibling keys for the same event.
        return Task.FromResult(TryGetFromAnyTemplateKey(settings, eventType, templateId));
    }

    private EventFieldMapping? TryGetFromAnyTemplateKey(
        IConfiguration settings,
        string eventType,
        string preferredTemplateId)
    {
        var root = settings.GetSection(SectionName);
        if (!root.Exists())
            return null;

        var matches = new List<(string TemplateKey, EventFieldMapping Mapping)>();

        foreach (var templateSection in root.GetChildren())
        {
            // Skip non-template keys such as BasePath.
            if (string.Equals(templateSection.Key, "BasePath", StringComparison.OrdinalIgnoreCase))
                continue;

            var mapping = TryGetFromSection(templateSection.GetSection(eventType), templateSection.Key, eventType);
            if (mapping is null)
                continue;

            matches.Add((templateSection.Key, mapping));
        }

        if (matches.Count == 0)
        {
            logger.LogWarning(
                "No EventMappings entry found for event {EventType} (requested template {TemplateId}).",
                eventType,
                preferredTemplateId);
            return null;
        }

        // Prefer a key of the "other" shape: the Admin GUID key when the runtime id is a
        // legacy string, and vice versa.
        var preferredIsGuid = Guid.TryParse(preferredTemplateId, out _);
        var preferredMatch = matches.FirstOrDefault(m =>
            Guid.TryParse(m.TemplateKey, out _) != preferredIsGuid
            || string.Equals(m.TemplateKey, preferredTemplateId, StringComparison.OrdinalIgnoreCase));

        var chosen = preferredMatch.Mapping is not null ? preferredMatch : matches[0];

        if (matches.Count > 1)
        {
            logger.LogWarning(
                "Multiple EventMappings entries found for event {EventType} under templates [{Keys}]. Using template key {ChosenKey}.",
                eventType,
                string.Join(", ", matches.Select(m => m.TemplateKey)),
                chosen.TemplateKey);
        }
        else
        {
            logger.LogInformation(
                "Resolved event mapping for {EventType} under TenantConfig template key {TemplateKey} (requested {RequestedTemplateId})",
                eventType,
                chosen.TemplateKey,
                preferredTemplateId);
        }

        return chosen.Mapping;
    }

    private EventFieldMapping? TryGetFromSection(IConfigurationSection section, string templateId, string eventType)
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
                    "EventMappings config for template {TemplateId} and event {EventType} deserialized to null. Raw JSON: {Json}",
                    templateId,
                    eventType,
                    json);
            }

            return mapping;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                ex,
                "Failed to deserialize EventMappings config for template {TemplateId} and event {EventType}. Raw JSON: {Json}",
                templateId,
                eventType,
                json);
            return null;
        }
    }
}
