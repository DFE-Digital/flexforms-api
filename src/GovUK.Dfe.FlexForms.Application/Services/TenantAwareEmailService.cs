using GovUK.Dfe.CoreLibs.Email.Interfaces;
using GovUK.Dfe.CoreLibs.Email.Models;
using Microsoft.AspNetCore.Http;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// <see cref="IEmailService"/> that sends via a per-tenant GOV.UK Notify client from TenantConfig.
/// Does not depend on the host CoreLibs email registration (host <c>test-api-key</c> must never
/// be constructed — Notify rejects it in <c>NotificationClient</c> ctor during DI).
/// </summary>
public sealed class TenantAwareEmailService(
    ITenantEmailServiceFactory tenantEmailFactory,
    IHttpContextAccessor httpContextAccessor) : IEmailService
{
    public string ProviderName => ResolveTenantEmail().ProviderName;

    public Task<EmailResponse> SendEmailAsync(EmailMessage message, CancellationToken cancellationToken = default)
        => ResolveTenantEmail().SendEmailAsync(message, cancellationToken);

    public Task<EmailResponse> GetEmailStatusAsync(string emailId, CancellationToken cancellationToken = default)
        => ResolveTenantEmail().GetEmailStatusAsync(emailId, cancellationToken);

    public Task<IEnumerable<EmailResponse>> GetEmailsAsync(
        string? reference = null,
        EmailStatus? status = null,
        DateTime? olderThan = null,
        CancellationToken cancellationToken = default)
        => ResolveTenantEmail().GetEmailsAsync(reference, status, olderThan, cancellationToken);

    public Task<EmailTemplate> GetTemplateAsync(string templateId, CancellationToken cancellationToken = default)
        => ResolveTenantEmail().GetTemplateAsync(templateId, cancellationToken);

    public Task<EmailTemplate> GetTemplateAsync(string templateId, int version, CancellationToken cancellationToken = default)
        => ResolveTenantEmail().GetTemplateAsync(templateId, version, cancellationToken);

    public Task<IEnumerable<EmailTemplate>> GetAllTemplatesAsync(
        string? templateType = null,
        CancellationToken cancellationToken = default)
        => ResolveTenantEmail().GetAllTemplatesAsync(templateType, cancellationToken);

    public Task<TemplatePreview> PreviewTemplateAsync(
        string templateId,
        Dictionary<string, object>? personalization = null,
        CancellationToken cancellationToken = default)
        => ResolveTenantEmail().PreviewTemplateAsync(templateId, personalization, cancellationToken);

    public bool IsValidEmail(string emailAddress)
        => ResolveTenantEmail().IsValidEmail(emailAddress);

    private IEmailService ResolveTenantEmail()
    {
        if (httpContextAccessor.HttpContext is null)
        {
            throw new InvalidOperationException("No HttpContext available for email operation");
        }

        return tenantEmailFactory.GetRequiredEmailService();
    }
}
