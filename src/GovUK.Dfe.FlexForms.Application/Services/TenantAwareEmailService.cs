using GovUK.Dfe.CoreLibs.Email.Interfaces;
using GovUK.Dfe.CoreLibs.Email.Models;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Decorator that refuses email operations when the current tenant's Email settings
/// are missing or incomplete. Does not read host GlobalConfiguration at runtime.
/// Resolves <see cref="ITenantContextAccessor"/> from the request scope so it can
/// safely decorate a singleton <see cref="IEmailService"/>.
/// </summary>
public sealed class TenantAwareEmailService(
    IEmailService inner,
    IHttpContextAccessor httpContextAccessor) : IEmailService
{
    public string ProviderName => inner.ProviderName;

    public Task<EmailResponse> SendEmailAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        EnsureTenantEmailConfigured();
        return inner.SendEmailAsync(message, cancellationToken);
    }

    public Task<EmailResponse> GetEmailStatusAsync(string emailId, CancellationToken cancellationToken = default)
    {
        EnsureTenantEmailConfigured();
        return inner.GetEmailStatusAsync(emailId, cancellationToken);
    }

    public Task<IEnumerable<EmailResponse>> GetEmailsAsync(
        string? reference = null,
        EmailStatus? status = null,
        DateTime? olderThan = null,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantEmailConfigured();
        return inner.GetEmailsAsync(reference, status, olderThan, cancellationToken);
    }

    public Task<EmailTemplate> GetTemplateAsync(string templateId, CancellationToken cancellationToken = default)
    {
        EnsureTenantEmailConfigured();
        return inner.GetTemplateAsync(templateId, cancellationToken);
    }

    public Task<EmailTemplate> GetTemplateAsync(string templateId, int version, CancellationToken cancellationToken = default)
    {
        EnsureTenantEmailConfigured();
        return inner.GetTemplateAsync(templateId, version, cancellationToken);
    }

    public Task<IEnumerable<EmailTemplate>> GetAllTemplatesAsync(
        string? templateType = null,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantEmailConfigured();
        return inner.GetAllTemplatesAsync(templateType, cancellationToken);
    }

    public Task<TemplatePreview> PreviewTemplateAsync(
        string templateId,
        Dictionary<string, object>? personalization = null,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantEmailConfigured();
        return inner.PreviewTemplateAsync(templateId, personalization, cancellationToken);
    }

    public bool IsValidEmail(string emailAddress)
    {
        EnsureTenantEmailConfigured();
        return inner.IsValidEmail(emailAddress);
    }

    private void EnsureTenantEmailConfigured()
    {
        var requestServices = httpContextAccessor.HttpContext?.RequestServices
            ?? throw new InvalidOperationException("No HttpContext available for email operation");

        var tenantContextAccessor = requestServices.GetRequiredService<ITenantContextAccessor>();
        TenantEmailConfiguration.EnsureConfigured(tenantContextAccessor);
    }
}
