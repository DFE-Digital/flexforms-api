using GovUK.Dfe.FlexForms.Application.Common.EventHandlers;
using GovUK.Dfe.FlexForms.Application.Options;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Events;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging;
using GovUK.Dfe.CoreLibs.Email.Interfaces;
using GovUK.Dfe.CoreLibs.Email.Models;

namespace GovUK.Dfe.FlexForms.Application.Applications.EventHandlers;

public sealed class ApplicationSubmittedEventHandler(
    ILogger<ApplicationSubmittedEventHandler> logger,
    IEmailService emailService,
    IEmailTemplateResolver emailTemplateResolver,
    IApplicationRepository applicationRepository,
    IEventTriggerDispatcher eventTriggerDispatcher) : BaseEventHandler<ApplicationSubmittedEvent>(logger)
{
    private readonly ILogger<ApplicationSubmittedEventHandler> _logger = logger;

    protected override async Task HandleEvent(ApplicationSubmittedEvent notification, CancellationToken cancellationToken)
    {
        await SendConfirmationEmailAsync(notification, cancellationToken);
        await DispatchConfiguredEventsAsync(notification, cancellationToken);
    }

    private async Task SendConfirmationEmailAsync(
        ApplicationSubmittedEvent notification,
        CancellationToken cancellationToken)
    {
        try
        {
            // Resolve the email template ID based on the application template and email type
            var emailTemplateId = await emailTemplateResolver.ResolveEmailTemplateAsync(
                notification.TemplateId,
                "ApplicationSubmitted");

            if (string.IsNullOrEmpty(emailTemplateId))
            {
                _logger.LogError("Could not resolve email template for application {ApplicationId} with template {TemplateId}", 
                    notification.ApplicationId.Value, notification.TemplateId.Value);
                return;
            }

            var email = new EmailMessage()
            {
                ToEmail = notification.UserEmail,
                TemplateId = emailTemplateId,
                Personalization = new Dictionary<string, object>
                {
                    ["user_full_name"] = notification.UserFullName,
                    ["application_reference"] = notification.ApplicationReference,
                    ["submitted_date"] = notification.SubmittedOn.ToString("dd/MM/yyyy"),
                    ["submitted_time"] = notification.SubmittedOn.ToString("HH:mm")
                }
            };

            var response = await emailService.SendEmailAsync(email, cancellationToken);

            if (response.Status == EmailStatus.Sent || response.Status == EmailStatus.Queued || response.Status == EmailStatus.Accepted)
            {
                _logger.LogInformation("Email sent successfully for submitted application {ApplicationId} (Reference: {ApplicationReference}) to {UserEmail}. Status: {EmailStatus}, Template: {TemplateId}",
                    notification.ApplicationId.Value, notification.ApplicationReference, notification.UserEmail, response.Status, emailTemplateId);
            }
            else
            {
                _logger.LogWarning("Failed to send email for submitted application {ApplicationId} (Reference: {ApplicationReference}) to {UserEmail}. Status: {EmailStatus}, Template: {TemplateId}",
                    notification.ApplicationId.Value, notification.ApplicationReference, notification.UserEmail, response.Status, emailTemplateId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email for submitted application {ApplicationId} (Reference: {ApplicationReference}) to {UserEmail}",
                notification.ApplicationId.Value, notification.ApplicationReference, notification.UserEmail);
            
            // Don't rethrow - email failures shouldn't break the application submission process
            // The application submission itself has already succeeded at this point
        }
    }

    /// <summary>
    /// Publishes the tenant's ApplicationSubmitted event bindings using the latest saved responses.
    /// </summary>
    private async Task DispatchConfiguredEventsAsync(
        ApplicationSubmittedEvent notification,
        CancellationToken cancellationToken)
    {
        try
        {
            var latestResponse = await applicationRepository.GetLatestResponseAsync(
                notification.ApplicationId,
                cancellationToken);

            var formData = ApplicationFormDataParser.Parse(latestResponse?.ResponseBody);

            var platformMetadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                [PlatformEventMetadataKeys.ApplicationId] = notification.ApplicationId.Value.ToString(),
                [PlatformEventMetadataKeys.ApplicationReference] = notification.ApplicationReference,
                [PlatformEventMetadataKeys.SubmittedByUserId] = notification.SubmittedBy.Value.ToString(),
                [PlatformEventMetadataKeys.SubmittedByEmail] = notification.UserEmail,
                [PlatformEventMetadataKeys.SubmittedByFullName] = notification.UserFullName,
                [PlatformEventMetadataKeys.SubmittedOn] = notification.SubmittedOn
            };

            await eventTriggerDispatcher.DispatchAsync(
                EventTriggerType.ApplicationSubmitted,
                notification.ApplicationId.Value,
                notification.ApplicationReference,
                notification.TemplateId.Value.ToString(),
                formData,
                platformMetadata,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error dispatching ApplicationSubmitted events for application {ApplicationId} (Reference: {ApplicationReference})",
                notification.ApplicationId.Value,
                notification.ApplicationReference);
        }
    }
}
