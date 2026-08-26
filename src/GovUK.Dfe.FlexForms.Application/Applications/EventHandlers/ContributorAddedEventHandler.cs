using GovUK.Dfe.FlexForms.Application.Common.EventHandlers;
using GovUK.Dfe.FlexForms.Application.Options;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Events;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.CoreLibs.Email.Interfaces;
using GovUK.Dfe.CoreLibs.Email.Models;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.Applications.EventHandlers;

public sealed class ContributorAddedEventHandler(
    ILogger<ContributorAddedEventHandler> logger,
    IEmailService emailService,
    IEmailTemplateResolver emailTemplateResolver,
    IEmailPersonalisationBuilder emailPersonalisationBuilder,
    IApplicationRepository applicationRepository) : BaseEventHandler<ContributorAddedEvent>(logger)
{
    protected override async Task HandleEvent(ContributorAddedEvent notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing contributor added event for {ContributorId} to application {ApplicationId} by {AddedBy}", 
            notification.Contributor.Id!.Value, 
            notification.ApplicationId.Value, 
            notification.AddedBy.Value);

        await SendContributorInvitationEmail(notification, cancellationToken);
    }

    private async Task SendContributorInvitationEmail(ContributorAddedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var emailTemplateId = await emailTemplateResolver.ResolveEmailTemplateAsync(
                notification.TemplateId,
                EmailTypes.ContributorInvited);

            if (string.IsNullOrEmpty(emailTemplateId))
            {
                logger.LogError("Could not resolve email template for contributor invitation to application {ApplicationId} with template {TemplateId}",
                    notification.ApplicationId.Value, notification.TemplateId.Value);
                return;
            }

            var latestResponse = await applicationRepository.GetLatestResponseAsync(
                notification.ApplicationId,
                cancellationToken);
            var formData = ApplicationFormDataParser.Parse(latestResponse?.ResponseBody);

            var baseline = new Dictionary<string, object>
            {
                ["contributor_name"] = notification.Contributor.Name,
                ["application_reference"] = notification.ApplicationReference,
                ["added_date"] = notification.AddedOn.ToString("dd/MM/yyyy"),
                ["added_time"] = notification.AddedOn.ToString("HH:mm")
            };

            var platformMetadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                [PlatformEventMetadataKeys.ApplicationId] = notification.ApplicationId.Value.ToString(),
                [PlatformEventMetadataKeys.ApplicationReference] = notification.ApplicationReference,
                [PlatformEventMetadataKeys.ContributorName] = notification.Contributor.Name,
                [PlatformEventMetadataKeys.ContributorEmail] = notification.Contributor.Email,
                [PlatformEventMetadataKeys.AddedOn] = notification.AddedOn
            };

            var personalization = await emailPersonalisationBuilder.BuildAsync(
                notification.TemplateId.Value.ToString(),
                EmailTypes.ContributorInvited,
                notification.ApplicationId.Value,
                notification.ApplicationReference,
                baseline,
                formData,
                platformMetadata,
                cancellationToken);

            var email = new EmailMessage()
            {
                ToEmail = notification.Contributor.Email,
                TemplateId = emailTemplateId,
                Personalization = personalization
            };

            var response = await emailService.SendEmailAsync(email, cancellationToken);

            if (response.Status == EmailStatus.Sent || response.Status == EmailStatus.Queued || response.Status == EmailStatus.Accepted)
            {
                logger.LogInformation("Contributor invitation email sent successfully for {ContributorEmail} added to application {ApplicationId} (Reference: {ApplicationReference}). Status: {EmailStatus}, Template: {TemplateId}",
                    notification.Contributor.Email, notification.ApplicationId.Value, notification.ApplicationReference, response.Status, emailTemplateId);
            }
            else
            {
                logger.LogWarning("Failed to send contributor invitation email for {ContributorEmail} added to application {ApplicationId} (Reference: {ApplicationReference}). Status: {EmailStatus}, Template: {TemplateId}",
                    notification.Contributor.Email, notification.ApplicationId.Value, notification.ApplicationReference, response.Status, emailTemplateId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending contributor invitation email for {ContributorEmail} added to application {ApplicationId} (Reference: {ApplicationReference})",
                notification.Contributor.Email, notification.ApplicationId.Value, notification.ApplicationReference);
            
            // Don't rethrow - email failures shouldn't break the contributor addition process
            // The contributor addition itself has already succeeded at this point
        }
    }
}
