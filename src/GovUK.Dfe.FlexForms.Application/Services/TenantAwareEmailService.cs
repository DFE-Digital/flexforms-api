using GovUK.Dfe.CoreLibs.Email.Interfaces;
using GovUK.Dfe.CoreLibs.Email.Models;
using Microsoft.AspNetCore.Http;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Decorator that sends email via a per-tenant GOV.UK Notify client built from TenantConfig.
/// Host <c>GlobalConfiguration:Email</c> (e.g. <c>test-api-key</c>) is not used at runtime.
/// The <paramref name="inner"/> parameter is required by Scrutor decorate and is unused for sends.
/// </summary>
public sealed class TenantAwareEmailService(
    IEmailService inner,
    ITenantEmailServiceFactory tenantEmailFactory,
    IHttpContextAccessor httpContextAccessor) : IEmailService
{
    // Keep reference so Scrutor / DI still wire the host chain for boot; never call it for sends.
    private readonly IEmailService _ = inner;

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
        // Factory requires HttpContext; fail early with the same message tests expect.
        if (httpContextAccessor.HttpContext is null)
        {
            throw new InvalidOperationException("No HttpContext available for email operation");
        }

        return tenantEmailFactory.GetRequiredEmailService();
    }
}
