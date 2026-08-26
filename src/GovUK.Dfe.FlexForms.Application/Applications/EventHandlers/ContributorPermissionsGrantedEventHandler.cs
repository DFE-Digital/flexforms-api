using GovUK.Dfe.FlexForms.Application.Common.EventHandlers;
using GovUK.Dfe.FlexForms.Application.Options;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Events;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging;
using GovUK.Dfe.CoreLibs.Email.Interfaces;
using GovUK.Dfe.CoreLibs.Email.Models;

namespace GovUK.Dfe.FlexForms.Application.Applications.EventHandlers;

public sealed class ContributorPermissionsGrantedEventHandler(
    ILogger<ContributorPermissionsGrantedEventHandler> logger,
    IEmailService emailService,
    IEmailTemplateResolver emailTemplateResolver,
    IEmailPersonalisationBuilder emailPersonalisationBuilder,
    IApplicationRepository applicationRepository) : BaseEventHandler<ContributorPermissionsGrantedEvent>(logger)
{
    protected override async Task HandleEvent(ContributorPermissionsGrantedEvent notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("Handling permissions granted for contributor {ContributorId} to application {ApplicationId} by {GrantedBy}",
            notification.Contributor.Id!.Value,
            notification.ApplicationId.Value,
            notification.GrantedBy.Value);

        logger.LogInformation("Successfully processed permissions granted for contributor {ContributorId} to application {ApplicationId}",
            notification.Contributor.Id!.Value,
            notification.ApplicationId.Value);

        await SendContributorAccessGrantedEmail(notification, cancellationToken);
    }

    private async Task SendContributorAccessGrantedEmail(ContributorPermissionsGrantedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            // Notify template ID still resolves via ContributorInvited; personalisation mapping uses ContributorAccessGranted.
            var emailTemplateId = await emailTemplateResolver.ResolveEmailTemplateAsync(
                notification.TemplateId,
                EmailTypes.ContributorInvited);

            if (string.IsNullOrEmpty(emailTemplateId))
            {
                logger.LogError("Could not resolve email template for contributor access granted to application {ApplicationId} with template {TemplateId}",
                    notification.ApplicationId.Value, notification.TemplateId.Value);
                return;
            }

            var latestResponse = await applicationRepository.GetLatestResponseAsync(
                notification.ApplicationId,
                cancellationToken);
            var formData = ApplicationFormDataParser.Parse(latestResponse?.ResponseBody);

            var accessTypes = string.Join(", ", notification.GrantedAccessTypes.Select(a => a.ToString()));

            var baseline = new Dictionary<string, object>
            {
                ["contributor_name"] = notification.Contributor.Name,
                ["application_reference"] = notification.ApplicationReference,
                ["granted_date"] = notification.GrantedOn.ToString("dd/MM/yyyy"),
                ["granted_time"] = notification.GrantedOn.ToString("HH:mm"),
                ["access_types"] = accessTypes
            };

            var platformMetadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                [PlatformEventMetadataKeys.ApplicationId] = notification.ApplicationId.Value.ToString(),
                [PlatformEventMetadataKeys.ApplicationReference] = notification.ApplicationReference,
                [PlatformEventMetadataKeys.ContributorName] = notification.Contributor.Name,
                [PlatformEventMetadataKeys.ContributorEmail] = notification.Contributor.Email,
                [PlatformEventMetadataKeys.GrantedOn] = notification.GrantedOn,
                [PlatformEventMetadataKeys.AccessTypes] = accessTypes
            };

            var personalization = await emailPersonalisationBuilder.BuildAsync(
                notification.TemplateId.Value.ToString(),
                EmailTypes.ContributorAccessGranted,
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
                logger.LogInformation("Contributor access granted email sent successfully for {ContributorEmail} for application {ApplicationId} (Reference: {ApplicationReference}). Status: {EmailStatus}, Template: {TemplateId}",
                    notification.Contributor.Email, notification.ApplicationId.Value, notification.ApplicationReference, response.Status, emailTemplateId);
            }
            else
            {
                logger.LogWarning("Failed to send contributor access granted email for {ContributorEmail} for application {ApplicationId} (Reference: {ApplicationReference}). Status: {EmailStatus}, Template: {TemplateId}",
                    notification.Contributor.Email, notification.ApplicationId.Value, notification.ApplicationReference, response.Status, emailTemplateId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending contributor access granted email for {ContributorEmail} for application {ApplicationId} (Reference: {ApplicationReference})",
                notification.Contributor.Email, notification.ApplicationId.Value, notification.ApplicationReference);

            // Don't rethrow - email failures shouldn't break the permission granting process
        }
    }
}
