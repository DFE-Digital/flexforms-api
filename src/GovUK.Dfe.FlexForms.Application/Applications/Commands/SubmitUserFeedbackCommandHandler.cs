using GovUK.Dfe.FlexForms.Application.Common.Attributes;
using GovUK.Dfe.FlexForms.Application.Common.Behaviours;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Email.Interfaces;
using GovUK.Dfe.CoreLibs.Email.Models;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using System;

namespace GovUK.Dfe.FlexForms.Application.Applications.Commands;

[RateLimit(1, 30)]
public sealed record SubmitUserFeedbackCommand(UserFeedbackRequest Request)
    : IRequest<Result<bool>>, IRateLimitedRequest;

public sealed class SubmitUserFeedbackCommandHandler(
    ILogger<SubmitUserFeedbackCommandHandler> logger,
    ITenantContextAccessor tenantContextAccessor,
    IEmailTemplateResolver emailTemplateResolver,
    IEmailService emailService) : IRequestHandler<SubmitUserFeedbackCommand, Result<bool>>
{
    private const string ServiceSupportEmailAddressConfigurationKey = "Email:ServiceSupportEmailAddress";

    public async Task<Result<bool>> Handle(SubmitUserFeedbackCommand request, CancellationToken cancellationToken)
    {
        var tenantConfig = tenantContextAccessor.CurrentTenant?.Settings
                           ?? throw new InvalidOperationException("Tenant configuration has not been resolved for user feedback handling.");
        var supportEmailAddress = tenantConfig.GetValue<string?>(ServiceSupportEmailAddressConfigurationKey);

        if (supportEmailAddress is null)
        {
            logger.LogError(
                "Service support email address is not configured - make sure that {ConfigKey} is set in appsettings.json or environment variables",
                ServiceSupportEmailAddressConfigurationKey);
            throw new InvalidOperationException("Service support email address is not configured");
        }

        var templateId = new TemplateId(request.Request.TemplateId);

        await SendSupportEmail(templateId, request.Request, supportEmailAddress, cancellationToken);

        var userEmailAddress = request.Request switch
        {
            BugReport bugReport => bugReport.EmailAddress,
            SupportRequest supportRequest => supportRequest.EmailAddress,
            _ => null
        };

        if (userEmailAddress is not null)
        {
            await SendUserEmail(templateId, request.Request, userEmailAddress, cancellationToken);
        }

        return Result<bool>.Success(true);
    }

    private async Task SendSupportEmail(TemplateId templateId, UserFeedbackRequest request, string emailAddress,
        CancellationToken cancellationToken)
    {
        var emailTemplateId =
            await emailTemplateResolver.ResolveEmailTemplateAsync(templateId, $"{request.Type}Internal");

        if (string.IsNullOrWhiteSpace(emailTemplateId))
        {
            throw new InvalidOperationException(
                $"Could not resolve GOV.UK Notify template for feedback type '{request.Type}Internal' " +
                $"and form template '{templateId.Value}'. Check ApplicationTemplates:HostMappings and EmailTemplates.");
        }

        var emailMessage = CreateEmailMessage(request, emailAddress, emailTemplateId);

        try
        {
            await emailService.SendEmailAsync(emailMessage, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not send support email");
            throw;
        }
    }

    private async Task SendUserEmail(TemplateId templateId, UserFeedbackRequest request, string emailAddress,
        CancellationToken cancellationToken)
    {
        var emailTemplateId = await emailTemplateResolver.ResolveEmailTemplateAsync(templateId, $"{request.Type}User");

        if (string.IsNullOrWhiteSpace(emailTemplateId))
        {
            throw new InvalidOperationException(
                $"Could not resolve GOV.UK Notify template for feedback type '{request.Type}User' " +
                $"and form template '{templateId.Value}'. Check ApplicationTemplates:HostMappings and EmailTemplates.");
        }

        var emailMessage = CreateEmailMessage(request, emailAddress, emailTemplateId);

        try
        {
            await emailService.SendEmailAsync(emailMessage, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not send user email");
            throw;
        }
    }

    private static EmailMessage CreateEmailMessage(UserFeedbackRequest request, string emailAddress, string? templateId)
    {
        var personalization = new Dictionary<string, object>
        {
            ["message"] = request.Message,
            ["reference_number"] = request.ReferenceNumber.ToDisplay(),
        };

        switch (request)
        {
            case BugReport bugReport:
                personalization.Add("contact_email", bugReport.EmailAddress.ToDisplay());
                break;
            case SupportRequest supportRequest:
                personalization.Add("contact_email", supportRequest.EmailAddress);
                break;
            case FeedbackOrSuggestion feedback:
                personalization.Add("satisfaction_score", feedback.SatisfactionScore.ToDisplay());
                break;
        }

        var emailMessage = new EmailMessage
        {
            ToEmail = emailAddress,
            TemplateId = templateId,
            Personalization = personalization
        };

        return emailMessage;
    }
}

internal static class MessageFormatExtensions
{
    internal static string ToDisplay(this SatisfactionScore score) => score switch
    {
        SatisfactionScore.VerySatisfied => "Very satisfied",
        SatisfactionScore.Satisfied => "Satisfied",
        SatisfactionScore.NeitherSatisfiedOrDissatisfied => "Neither satisfied or dissatisfied",
        SatisfactionScore.Dissatisfied => "Dissatisfied",
        SatisfactionScore.VeryDissatisfied => "Very dissatisfied",
        _ => throw new ArgumentOutOfRangeException(nameof(score), score, null)
    };

    internal static string ToDisplay(this string? s) => s ?? "Not provided";
}
