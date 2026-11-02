using System.Text.Json;
using GovUK.Dfe.FlexForms.Application.Common.Models;
using GovUK.Dfe.FlexForms.Application.Templates.QueryObjects;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Resolves GOV.UK Notify template IDs from the form template JSON first, then the tenant's
/// EmailTemplates setting. Product / form keys come only from configuration — never from
/// hardcoded service names.
/// </summary>
public class EmailTemplateResolver(
    ITenantContextAccessor tenantContextAccessor,
    IEaRepository<TemplateVersion> templateVersionRepository,
    ILogger<EmailTemplateResolver> logger) : IEmailTemplateResolver
{
    public async Task<string?> ResolveEmailTemplateAsync(TemplateId templateId, string emailType)
    {
        var fromTemplate = await TryGetFromTemplateJsonAsync(templateId, emailType);
        if (!string.IsNullOrWhiteSpace(fromTemplate))
        {
            logger.LogDebug(
                "Resolved email template {NotifyTemplateId} from form template JSON for template {TemplateId} and email type {EmailType}",
                fromTemplate,
                templateId.Value,
                emailType);
            return fromTemplate;
        }

        var tenant = tenantContextAccessor.CurrentTenant
            ?? throw new InvalidOperationException("No tenant context available for email template resolution.");

        var (appTemplatesConfig, emailTemplatesConfig) = GetTenantConfigs(tenant);

        var applicationType = ResolveApplicationType(templateId.Value, tenant, appTemplatesConfig, emailTemplatesConfig);

        if (string.IsNullOrEmpty(applicationType))
        {
            logger.LogWarning(
                "Could not determine application type for template ID {TemplateId}. " +
                "Ensure ApplicationTemplates:HostMappings includes this form template GUID, " +
                "or that EmailTemplates has a single product key / a key matching the tenant name.",
                templateId.Value);
            return null;
        }

        var emailTemplateId = emailTemplatesConfig.GetTemplateId(applicationType, emailType);

        if (string.IsNullOrEmpty(emailTemplateId))
        {
            logger.LogWarning(
                "Could not find email template for application type {ApplicationType} and email type {EmailType}",
                applicationType,
                emailType);
            return null;
        }

        logger.LogDebug(
            "Resolved email template {TemplateId} for application type {ApplicationType} and email type {EmailType}",
            emailTemplateId,
            applicationType,
            emailType);

        return emailTemplateId;
    }

    public Task<string?> ResolveTenantEmailTemplateAsync(string emailType)
    {
        var tenant = tenantContextAccessor.CurrentTenant
            ?? throw new InvalidOperationException("No tenant context available for email template resolution.");

        var (_, emailTemplatesConfig) = GetTenantConfigs(tenant);

        var tenantNameKey = emailTemplatesConfig.FindApplicationTypeKey(tenant.Name);
        var candidateKeys = tenantNameKey is null
            ? emailTemplatesConfig.Keys
            : emailTemplatesConfig.Keys.Where(k => k != tenantNameKey).Prepend(tenantNameKey);

        foreach (var applicationType in candidateKeys)
        {
            var emailTemplateId = emailTemplatesConfig.GetTemplateId(applicationType, emailType);
            if (!string.IsNullOrEmpty(emailTemplateId))
            {
                logger.LogDebug(
                    "Resolved tenant-wide email template {TemplateId} from application type {ApplicationType} for email type {EmailType}",
                    emailTemplateId,
                    applicationType,
                    emailType);
                return Task.FromResult<string?>(emailTemplateId);
            }
        }

        logger.LogWarning(
            "Could not find a tenant-wide email template for email type {EmailType} in tenant {TenantName}. " +
            "Add it under any EmailTemplates product key in the tenant configuration.",
            emailType,
            tenant.Name);
        return Task.FromResult<string?>(null);
    }

    public Task<string?> GetApplicationTypeAsync(TemplateId templateId)
    {
        var tenant = tenantContextAccessor.CurrentTenant
            ?? throw new InvalidOperationException("No tenant context available for email template resolution.");

        var (appTemplatesConfig, emailTemplatesConfig) = GetTenantConfigs(tenant);
        var applicationType = ResolveApplicationType(templateId.Value, tenant, appTemplatesConfig, emailTemplatesConfig);
        return Task.FromResult(applicationType);
    }

    private async Task<string?> TryGetFromTemplateJsonAsync(TemplateId templateId, string emailType)
    {
        try
        {
            var latest = await new GetLatestTemplateVersionForTemplateQueryObject(templateId)
                .Apply(templateVersionRepository.Query().AsNoTracking())
                .FirstOrDefaultAsync();

            if (latest is null || string.IsNullOrWhiteSpace(latest.JsonSchema))
                return null;

            using var doc = JsonDocument.Parse(latest.JsonSchema);
            if (!TryGetPropertyIgnoreCase(doc.RootElement, "emailTemplates", out var map)
                || map.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property in map.EnumerateObject())
            {
                if (!string.Equals(property.Name, emailType, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (property.Value.ValueKind != JsonValueKind.String)
                    return null;

                var value = property.Value.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not read emailTemplates from form template {TemplateId}",
                templateId.Value);
        }

        return null;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement root, string name, out JsonElement property)
    {
        if (root.TryGetProperty(name, out property))
            return true;

        foreach (var candidate in root.EnumerateObject())
        {
            if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }

    private static (ApplicationTemplatesConfiguration appTemplates, EmailTemplatesConfiguration emailTemplates)
        GetTenantConfigs(TenantConfiguration tenant)
    {
        var appTemplatesConfig = new ApplicationTemplatesConfiguration();
        tenant.Settings.GetSection("ApplicationTemplates").Bind(appTemplatesConfig);

        var emailTemplatesConfig = new EmailTemplatesConfiguration();
        tenant.Settings.GetSection("EmailTemplates").Bind(emailTemplatesConfig);

        return (appTemplatesConfig, emailTemplatesConfig);
    }

    private string? ResolveApplicationType(
        Guid templateId,
        TenantConfiguration tenant,
        ApplicationTemplatesConfiguration appTemplatesConfig,
        EmailTemplatesConfiguration emailTemplatesConfig)
    {
        var fromHostMappings = GetApplicationTypeCandidateByTemplateId(templateId, appTemplatesConfig);
        if (!string.IsNullOrEmpty(fromHostMappings))
        {
            // Prefer the EmailTemplates key casing when a case-insensitive match exists.
            return emailTemplatesConfig.FindApplicationTypeKey(fromHostMappings) ?? fromHostMappings;
        }

        // Single-product tenants often have EmailTemplates but incomplete HostMappings in Azure.
        if (emailTemplatesConfig.Count == 1)
        {
            var soleKey = emailTemplatesConfig.Keys.First();
            logger.LogWarning(
                "Template ID {TemplateId} not found in ApplicationTemplates:HostMappings; " +
                "falling back to sole EmailTemplates key {ApplicationType}.",
                templateId,
                soleKey);
            return soleKey;
        }

        var fromTenantName = emailTemplatesConfig.FindApplicationTypeKey(tenant.Name);
        if (!string.IsNullOrEmpty(fromTenantName))
        {
            logger.LogWarning(
                "Template ID {TemplateId} not found in ApplicationTemplates:HostMappings; " +
                "falling back to tenant name EmailTemplates key {ApplicationType}.",
                templateId,
                fromTenantName);
            return fromTenantName;
        }

        logger.LogWarning("Template ID {TemplateId} not found in host mappings", templateId);
        return null;
    }

    private static string? GetApplicationTypeCandidateByTemplateId(
        Guid templateId,
        ApplicationTemplatesConfiguration appTemplatesConfig)
    {
        var templateIdString = templateId.ToString();

        var mapping = appTemplatesConfig.HostMappings
            .FirstOrDefault(kvp => string.Equals(kvp.Value, templateIdString, StringComparison.OrdinalIgnoreCase));

        if (mapping.Key is null)
            return null;

        return ConvertHostMappingToApplicationType(mapping.Key);
    }

    /// <summary>
    /// Derives an EmailTemplates product-key candidate from a HostMappings key.
    /// Keys may be short names or FQDNs; the first DNS label is used, then matched
    /// case-insensitively against configured EmailTemplates keys.
    /// </summary>
    internal static string ConvertHostMappingToApplicationType(string hostMapping)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostMapping);

        var key = hostMapping.Trim();
        var dot = key.IndexOf('.');
        if (dot > 0)
            key = key[..dot];

        if (key.Length == 0)
            return hostMapping.Trim();

        return char.ToUpperInvariant(key[0]) + key[1..];
    }
}
