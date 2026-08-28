using GovUK.Dfe.FlexForms.Application.Common.Models;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Resolves GOV.UK Notify template IDs from the current tenant's settings.
/// Product / form keys come only from configuration — never from hardcoded service names.
/// </summary>
public class EmailTemplateResolver(
    ITenantContextAccessor tenantContextAccessor,
    ILogger<EmailTemplateResolver> logger) : IEmailTemplateResolver
{
    public Task<string?> ResolveEmailTemplateAsync(TemplateId templateId, string emailType)
    {
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
            return Task.FromResult<string?>(null);
        }

        var emailTemplateId = emailTemplatesConfig.GetTemplateId(applicationType, emailType);

        if (string.IsNullOrEmpty(emailTemplateId))
        {
            logger.LogWarning(
                "Could not find email template for application type {ApplicationType} and email type {EmailType}",
                applicationType,
                emailType);
            return Task.FromResult<string?>(null);
        }

        logger.LogDebug(
            "Resolved email template {TemplateId} for application type {ApplicationType} and email type {EmailType}",
            emailTemplateId,
            applicationType,
            emailType);

        return Task.FromResult<string?>(emailTemplateId);
    }

    public Task<string?> GetApplicationTypeAsync(TemplateId templateId)
    {
        var tenant = tenantContextAccessor.CurrentTenant
            ?? throw new InvalidOperationException("No tenant context available for email template resolution.");

        var (appTemplatesConfig, emailTemplatesConfig) = GetTenantConfigs(tenant);
        var applicationType = ResolveApplicationType(templateId.Value, tenant, appTemplatesConfig, emailTemplatesConfig);
        return Task.FromResult(applicationType);
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
