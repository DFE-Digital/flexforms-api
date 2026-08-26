using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.FlexForms.Application.Applications.EventHandlers;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Events;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Email.Interfaces;
using GovUK.Dfe.CoreLibs.Email.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Tests.EventHandlers;

public class ContributorPermissionsGrantedEventHandlerTests
{
    private readonly ILogger<ContributorPermissionsGrantedEventHandler> _logger;
    private readonly IEmailService _emailService;
    private readonly IEmailTemplateResolver _emailTemplateResolver;
    private readonly IEmailPersonalisationBuilder _emailPersonalisationBuilder;
    private readonly IApplicationRepository _applicationRepository;
    private readonly ContributorPermissionsGrantedEventHandler _handler;

    public ContributorPermissionsGrantedEventHandlerTests()
    {
        _logger = Substitute.For<ILogger<ContributorPermissionsGrantedEventHandler>>();
        _emailService = Substitute.For<IEmailService>();
        _emailTemplateResolver = Substitute.For<IEmailTemplateResolver>();
        _emailPersonalisationBuilder = Substitute.For<IEmailPersonalisationBuilder>();
        _applicationRepository = Substitute.For<IApplicationRepository>();

        _emailPersonalisationBuilder.BuildAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<Dictionary<string, object>>(),
                Arg.Any<Dictionary<string, object>>(),
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(new Dictionary<string, object>(ci.ArgAt<Dictionary<string, object>>(4))));

        _handler = new ContributorPermissionsGrantedEventHandler(
            _logger,
            _emailService,
            _emailTemplateResolver,
            _emailPersonalisationBuilder,
            _applicationRepository);
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ShouldSendEmail_WhenTemplateResolved(
        User contributor,
        ApplicationId applicationId,
        string applicationReference,
        TemplateId templateId,
        UserId grantedBy)
    {
        // Arrange
        var grantedOn = new DateTime(2024, 1, 15, 10, 30, 0);
        var accessTypes = new[] { AccessType.Read, AccessType.Write };
        var @event = new ContributorPermissionsGrantedEvent(
            applicationId, applicationReference, templateId, contributor, accessTypes, grantedBy, grantedOn);

        var expectedTemplateId = "template-123";
        _emailTemplateResolver.ResolveEmailTemplateAsync(templateId, "ContributorInvited")
            .Returns(expectedTemplateId);

        var successResponse = new EmailResponse { Id = "email-1", Status = EmailStatus.Sent };
        _emailService.SendEmailAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(successResponse);

        // Act
        await _handler.Handle(@event, CancellationToken.None);

        // Assert
        await _emailService.Received(1).SendEmailAsync(
            Arg.Is<EmailMessage>(email =>
                email.ToEmail == contributor.Email &&
                email.TemplateId == expectedTemplateId &&
                email.Personalization["contributor_name"].ToString() == contributor.Name &&
                email.Personalization["application_reference"].ToString() == applicationReference &&
                email.Personalization["granted_date"].ToString() == "15/01/2024" &&
                email.Personalization["granted_time"].ToString() == "10:30"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ShouldNotSendEmail_WhenTemplateCannotBeResolved(
        User contributor,
        ApplicationId applicationId,
        string applicationReference,
        TemplateId templateId,
        UserId grantedBy)
    {
        // Arrange
        var accessTypes = new[] { AccessType.Read };
        var @event = new ContributorPermissionsGrantedEvent(
            applicationId, applicationReference, templateId, contributor, accessTypes, grantedBy, DateTime.UtcNow);

        _emailTemplateResolver.ResolveEmailTemplateAsync(templateId, "ContributorInvited")
            .Returns((string?)null);

        // Act
        await _handler.Handle(@event, CancellationToken.None);

        // Assert
        await _emailService.DidNotReceive().SendEmailAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());

        _logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Could not resolve email template")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ShouldLogWarning_WhenEmailFails(
        User contributor,
        ApplicationId applicationId,
        string applicationReference,
        TemplateId templateId,
        UserId grantedBy)
    {
        // Arrange
        var accessTypes = new[] { AccessType.Read };
        var @event = new ContributorPermissionsGrantedEvent(
            applicationId, applicationReference, templateId, contributor, accessTypes, grantedBy, DateTime.UtcNow);

        _emailTemplateResolver.ResolveEmailTemplateAsync(templateId, "ContributorInvited")
            .Returns("template-123");

        var failureResponse = new EmailResponse { Id = "email-1", Status = EmailStatus.PermanentFailure };
        _emailService.SendEmailAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(failureResponse);

        // Act
        await _handler.Handle(@event, CancellationToken.None);

        // Assert
        _logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Failed to send contributor access granted email")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ShouldLogError_AndNotThrow_WhenExceptionOccurs(
        User contributor,
        ApplicationId applicationId,
        string applicationReference,
        TemplateId templateId,
        UserId grantedBy)
    {
        // Arrange
        var accessTypes = new[] { AccessType.Read };
        var @event = new ContributorPermissionsGrantedEvent(
            applicationId, applicationReference, templateId, contributor, accessTypes, grantedBy, DateTime.UtcNow);

        _emailTemplateResolver.ResolveEmailTemplateAsync(templateId, "ContributorInvited")
            .Returns("template-123");

        _emailService.SendEmailAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<EmailResponse>(new Exception("SMTP down")));

        // Act & Assert
        var exception = await Record.ExceptionAsync(() => _handler.Handle(@event, CancellationToken.None));
        Assert.Null(exception);

        _logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Error sending contributor access granted email")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_ShouldIncludeAccessTypes_InEmailPersonalization(
        User contributor,
        ApplicationId applicationId,
        string applicationReference,
        TemplateId templateId,
        UserId grantedBy)
    {
        // Arrange
        var accessTypes = new[] { AccessType.Read, AccessType.Write, AccessType.Delete };
        var @event = new ContributorPermissionsGrantedEvent(
            applicationId, applicationReference, templateId, contributor, accessTypes, grantedBy, DateTime.UtcNow);

        _emailTemplateResolver.ResolveEmailTemplateAsync(templateId, "ContributorInvited")
            .Returns("template-123");

        var successResponse = new EmailResponse { Id = "email-1", Status = EmailStatus.Sent };
        _emailService.SendEmailAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(successResponse);

        // Act
        await _handler.Handle(@event, CancellationToken.None);

        // Assert
        await _emailService.Received(1).SendEmailAsync(
            Arg.Is<EmailMessage>(email =>
                email.Personalization["access_types"].ToString()!.Contains("Read") &&
                email.Personalization["access_types"].ToString()!.Contains("Write") &&
                email.Personalization["access_types"].ToString()!.Contains("Delete")),
            Arg.Any<CancellationToken>());
    }
}
