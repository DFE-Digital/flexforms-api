using GovUK.Dfe.FlexForms.Application.Options;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Reads SchemaEvents definitions from the current tenant's settings.
/// </summary>
public sealed class SchemaEventDefinitionProvider(
    ITenantContextAccessor tenantContextAccessor) : ISchemaEventDefinitionProvider
{
    public const string SectionName = SchemaEventsOptions.SectionName;

    /// <inheritdoc />
    public SchemaEventDefinitionOptions? GetDefinition(string messageType)
    {
        if (string.IsNullOrWhiteSpace(messageType))
            return null;

        return GetAll().TryGetValue(messageType.Trim(), out var definition) ? definition : null;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, SchemaEventDefinitionOptions> GetAll()
    {
        var result = new Dictionary<string, SchemaEventDefinitionOptions>(StringComparer.OrdinalIgnoreCase);

        var section = tenantContextAccessor.CurrentTenant?.Settings.GetSection(SectionName);
        if (section is null || !section.Exists())
            return result;

        foreach (var child in section.GetChildren())
        {
            var definition = new SchemaEventDefinitionOptions();
            child.Bind(definition);

            if (string.IsNullOrWhiteSpace(definition.TopicName))
                definition.TopicName = child["topicName"] ?? child["TopicName"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(definition.Version))
                definition.Version = child["version"] ?? child["Version"] ?? "1.0";
            definition.Description ??= child["description"] ?? child["Description"];

            if (string.IsNullOrWhiteSpace(definition.TopicName))
                continue;

            result[child.Key] = definition;
        }

        return result;
    }
}
