using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.FlexForms.Application.Applications.EventHandlers;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Events;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;
using GovUK.Dfe.CoreLibs.Email.Interfaces;
using GovUK.Dfe.CoreLibs.Email.Models;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.Applications;

public class ApplicationSubmittedEventHandlerTests
{
    private static ApplicationSubmittedEventHandler CreateHandler(
        ILogger<ApplicationSubmittedEventHandler> logger,
        IEmailService emailService,
        IEmailTemplateResolver emailTemplateResolver)
        => new(
            logger,
            emailService,
            emailTemplateResolver,
            Substitute.For<IApplicationRepository>(),
            Substitute.For<IEventTriggerDispatcher>());

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldSendEmail_WhenEventReceived(
        ApplicationId applicationId,
        string applicationReference,
        TemplateId templateId,
        UserId submittedBy,
        string userEmail,
        string userFullName,
        DateTime submittedOn)
    {
        // Arrange
        var logger = Substitute.For<ILogger<ApplicationSubmittedEventHandler>>();
        var emailService = Substitute.For<IEmailService>();
        var emailTemplateResolver = Substitute.For<IEmailTemplateResolver>();

        var @event = new ApplicationSubmittedEvent(
            applicationId, 
            applicationReference, 
            templateId, 
            submittedBy, 
            userEmail, 
            userFullName, 
            submittedOn);
            
        var expectedTemplateId = "a4188604-0053-4d77-9ad2-720f6fbbdf0a";
        emailTemplateResolver.ResolveEmailTemplateAsync(templateId, "ApplicationSubmitted")
            .Returns(expectedTemplateId);

        var successResponse = new EmailResponse { Id = "test-email-id", Status = EmailStatus.Sent };
        emailService.SendEmailAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(successResponse);

        var handler = CreateHandler(logger, emailService, emailTemplateResolver);

        // Act
        await handler.Handle(@event, CancellationToken.None);

        // Assert
        await emailService.Received(1).SendEmailAsync(
            Arg.Is<EmailMessage>(email =>
                email.ToEmail == userEmail &&
                email.TemplateId == expectedTemplateId &&
                email.Personalization["user_full_name"].ToString() == userFullName &&
                email.Personalization["application_reference"].ToString() == applicationReference &&
                email.Personalization.ContainsKey("submitted_date") &&
                email.Personalization.ContainsKey("submitted_time")),
            Arg.Any<CancellationToken>());

        await emailTemplateResolver.Received(1).ResolveEmailTemplateAsync(templateId, "ApplicationSubmitted");
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldFormatDateAndTimeCorrectly(
        ApplicationId applicationId,
        string applicationReference,
        TemplateId templateId,
        UserId submittedBy,
        string userEmail,
        string userFullName)
    {
        // Arrange
        var logger = Substitute.For<ILogger<ApplicationSubmittedEventHandler>>();
        var emailService = Substitute.For<IEmailService>();
        var emailTemplateResolver = Substitute.For<IEmailTemplateResolver>();

        var submittedOn = new DateTime(2023, 12, 25, 14, 30, 0);
        var @event = new ApplicationSubmittedEvent(
            applicationId, 
            applicationReference, 
            templateId, 
            submittedBy, 
            userEmail, 
            userFullName, 
            submittedOn);

        var expectedTemplateId = "a4188604-0053-4d77-9ad2-720f6fbbdf0a";
        emailTemplateResolver.ResolveEmailTemplateAsync(templateId, "ApplicationSubmitted")
            .Returns(expectedTemplateId);

        var successResponse = new EmailResponse { Id = "test-email-id", Status = EmailStatus.Sent };
        emailService.SendEmailAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(successResponse);

        var handler = CreateHandler(logger, emailService, emailTemplateResolver);

        // Act
        await handler.Handle(@event, CancellationToken.None);

        // Assert
        await emailService.Received(1).SendEmailAsync(
            Arg.Is<EmailMessage>(email =>
                email.Personalization["user_full_name"].ToString() == userFullName &&
                email.Personalization["application_reference"].ToString() == applicationReference &&
                email.Personalization["submitted_date"].ToString() == "25/12/2023" &&
                email.Personalization["submitted_time"].ToString() == "14:30"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldLogError_WhenEmailTemplateCannotBeResolved(
        ApplicationId applicationId,
        string applicationReference,
        TemplateId templateId,
        UserId submittedBy,
        string userEmail,
        string userFullName,
        DateTime submittedOn)
    {
        // Arrange
        var logger = Substitute.For<ILogger<ApplicationSubmittedEventHandler>>();
        var emailService = Substitute.For<IEmailService>();
        var emailTemplateResolver = Substitute.For<IEmailTemplateResolver>();

        var @event = new ApplicationSubmittedEvent(
            applicationId, 
            applicationReference, 
            templateId, 
            submittedBy, 
            userEmail, 
            userFullName, 
            submittedOn);

        emailTemplateResolver.ResolveEmailTemplateAsync(templateId, "ApplicationSubmitted")
            .Returns((string?)null);

        var handler = CreateHandler(logger, emailService, emailTemplateResolver);

        // Act
        await handler.Handle(@event, CancellationToken.None);

        // Assert
        await emailService.DidNotReceive().SendEmailAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
        
        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Could not resolve email template")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldLogSuccess_WhenEmailSentSuccessfully(
        ApplicationId applicationId,
        string applicationReference,
        TemplateId templateId,
        UserId submittedBy,
        string userEmail,
        string userFullName,
        DateTime submittedOn)
    {
        // Arrange
        var logger = Substitute.For<ILogger<ApplicationSubmittedEventHandler>>();
        var emailService = Substitute.For<IEmailService>();
        var emailTemplateResolver = Substitute.For<IEmailTemplateResolver>();

        var @event = new ApplicationSubmittedEvent(
            applicationId, 
            applicationReference, 
            templateId, 
            submittedBy, 
            userEmail, 
            userFullName, 
            submittedOn);

        var expectedTemplateId = "a4188604-0053-4d77-9ad2-720f6fbbdf0a";
        emailTemplateResolver.ResolveEmailTemplateAsync(templateId, "ApplicationSubmitted")
            .Returns(expectedTemplateId);

        var successResponse = new EmailResponse { Id = "test-email-id", Status = EmailStatus.Sent };
        emailService.SendEmailAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(successResponse);

        var handler = CreateHandler(logger, emailService, emailTemplateResolver);

        // Act
        await handler.Handle(@event, CancellationToken.None);

        // Assert
        logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Email sent successfully")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldLogWarning_WhenEmailFails(
        ApplicationId applicationId,
        string applicationReference,
        TemplateId templateId,
        UserId submittedBy,
        string userEmail,
        string userFullName,
        DateTime submittedOn)
    {
        // Arrange
        var logger = Substitute.For<ILogger<ApplicationSubmittedEventHandler>>();
        var emailService = Substitute.For<IEmailService>();
        var emailTemplateResolver = Substitute.For<IEmailTemplateResolver>();

        var @event = new ApplicationSubmittedEvent(
            applicationId, 
            applicationReference, 
            templateId, 
            submittedBy, 
            userEmail, 
            userFullName, 
            submittedOn);

        var expectedTemplateId = "a4188604-0053-4d77-9ad2-720f6fbbdf0a";
        emailTemplateResolver.ResolveEmailTemplateAsync(templateId, "ApplicationSubmitted")
            .Returns(expectedTemplateId);

        var failureResponse = new EmailResponse { Id = "test-email-failure-id", Status = EmailStatus.PermanentFailure };
        emailService.SendEmailAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(failureResponse);

        var handler = CreateHandler(logger, emailService, emailTemplateResolver);

        // Act
        await handler.Handle(@event, CancellationToken.None);

        // Assert
        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Failed to send email")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldLogErrorAndNotThrow_WhenExceptionOccurs(
        ApplicationId applicationId,
        string applicationReference,
        TemplateId templateId,
        UserId submittedBy,
        string userEmail,
        string userFullName,
        DateTime submittedOn)
    {
        // Arrange
        var logger = Substitute.For<ILogger<ApplicationSubmittedEventHandler>>();
        var emailService = Substitute.For<IEmailService>();
        var emailTemplateResolver = Substitute.For<IEmailTemplateResolver>();

        var @event = new ApplicationSubmittedEvent(
            applicationId, 
            applicationReference, 
            templateId, 
            submittedBy, 
            userEmail, 
            userFullName, 
            submittedOn);

        var expectedTemplateId = "a4188604-0053-4d77-9ad2-720f6fbbdf0a";
        emailTemplateResolver.ResolveEmailTemplateAsync(templateId, "ApplicationSubmitted")
            .Returns(expectedTemplateId);

        emailService.SendEmailAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Test exception"));

        var handler = CreateHandler(logger, emailService, emailTemplateResolver);

        // Act & Assert
        var exception = await Record.ExceptionAsync(async () => await handler.Handle(@event, CancellationToken.None));
        Assert.Null(exception); // Should not throw

        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Error sending email")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}