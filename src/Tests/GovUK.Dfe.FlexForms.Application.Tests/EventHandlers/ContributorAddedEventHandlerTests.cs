using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.FlexForms.Application.Applications.EventHandlers;
using GovUK.Dfe.FlexForms.Application.Options;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Events;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;
using Microsoft.Extensions.Logging;
using MockQueryable;
using NSubstitute;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;
using GovUK.Dfe.CoreLibs.Email.Interfaces;
using GovUK.Dfe.CoreLibs.Email.Models;

namespace GovUK.Dfe.FlexForms.Application.Tests.EventHandlers;

public class ContributorAddedEventHandlerTests
{
    private readonly ILogger<ContributorAddedEventHandler> _logger;
    private readonly IEmailService _emailService;
    private readonly IEmailTemplateResolver _emailTemplateResolver;
    private readonly IEmailPersonalisationBuilder _emailPersonalisationBuilder;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IEaRepository<User> _userRepository;
    private readonly ContributorAddedEventHandler _handler;

    public ContributorAddedEventHandlerTests()
    {
        _logger = Substitute.For<ILogger<ContributorAddedEventHandler>>();
        _emailService = Substitute.For<IEmailService>();
        _emailTemplateResolver = Substitute.For<IEmailTemplateResolver>();
        _emailPersonalisationBuilder = Substitute.For<IEmailPersonalisationBuilder>();
        _applicationRepository = Substitute.For<IApplicationRepository>();
        _userRepository = Substitute.For<IEaRepository<User>>();

        _applicationRepository.Query().Returns(Array.Empty<Domain.Entities.Application>().AsQueryable().BuildMock());
        _userRepository.Query().Returns(Array.Empty<User>().AsQueryable().BuildMock());

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

        _handler = new ContributorAddedEventHandler(
            _logger,
            _emailService,
            _emailTemplateResolver,
            _emailPersonalisationBuilder,
            _applicationRepository,
            _userRepository);
    }


    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_Should_Send_Contributor_Invitation_Email(
        User contributor,
        ApplicationId applicationId,
        string applicationReference,
        TemplateId templateId,
        UserId addedBy,
        DateTime addedOn)
    {
        // Arrange
        var @event = new ContributorAddedEvent(applicationId, applicationReference, templateId, contributor, addedBy, addedOn);
        
        var expectedTemplateId = "3a0e2130-ceea-48c9-8e3d-906431acc86f";
        _emailTemplateResolver.ResolveEmailTemplateAsync(templateId, "ContributorInvited")
            .Returns(expectedTemplateId);

        var successResponse = new EmailResponse { Id = "test-email-id", Status = EmailStatus.Sent };
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
                email.Personalization.ContainsKey(LeadApplicantLookup.BaselinePlaceholderKey) &&
                email.Personalization.ContainsKey("added_date") &&
                email.Personalization.ContainsKey("added_time")),
            Arg.Any<CancellationToken>());

        await _emailTemplateResolver.Received(1).ResolveEmailTemplateAsync(templateId, "ContributorInvited");
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_Should_Format_Date_And_Time_Correctly_In_Email(
        User contributor,
        ApplicationId applicationId,
        string applicationReference,
        TemplateVersionId templateVersionId,
        TemplateId templateId,
        UserId addedBy)
    {
        // Arrange
        var addedOn = new DateTime(2023, 12, 25, 14, 30, 0);
        var @event = new ContributorAddedEvent(applicationId, applicationReference, templateId, contributor, addedBy, addedOn);
        
        var expectedTemplateId = "3a0e2130-ceea-48c9-8e3d-906431acc86f";
        _emailTemplateResolver.ResolveEmailTemplateAsync(templateId, "ContributorInvited")
            .Returns(expectedTemplateId);

        var successResponse = new EmailResponse { Id = "test-email-id", Status = EmailStatus.Sent };
        _emailService.SendEmailAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(successResponse);

        // Act
        await _handler.Handle(@event, CancellationToken.None);

        // Assert
        await _emailService.Received(1).SendEmailAsync(
            Arg.Is<EmailMessage>(email =>
                email.Personalization["contributor_name"].ToString() == contributor.Name &&
                email.Personalization["application_reference"].ToString() == applicationReference &&
                email.Personalization["added_date"].ToString() == "25/12/2023" &&
                email.Personalization["added_time"].ToString() == "14:30"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_Should_Log_Error_When_Email_Template_Cannot_Be_Resolved(
        User contributor,
        ApplicationId applicationId,
        string applicationReference,
        TemplateId templateId,
        UserId addedBy,
        DateTime addedOn)
    {
        // Arrange
        var @event = new ContributorAddedEvent(applicationId, applicationReference, templateId, contributor, addedBy, addedOn);

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
    public async Task Handle_Should_Log_Success_When_Email_Sent_Successfully(
        User contributor,
        ApplicationId applicationId,
        string applicationReference,
        TemplateId templateId,
        UserId addedBy,
        DateTime addedOn)
    {
        // Arrange
        var @event = new ContributorAddedEvent(applicationId, applicationReference, templateId, contributor, addedBy, addedOn);
        
        var expectedTemplateId = "3a0e2130-ceea-48c9-8e3d-906431acc86f";
        _emailTemplateResolver.ResolveEmailTemplateAsync(templateId, "ContributorInvited")
            .Returns(expectedTemplateId);

        var successResponse = new EmailResponse { Id = "test-email-id", Status = EmailStatus.Sent };
        _emailService.SendEmailAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(successResponse);

        // Act
        await _handler.Handle(@event, CancellationToken.None);

        // Assert
        _logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Contributor invitation email sent successfully")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_Should_Log_Warning_When_Email_Fails(
        User contributor,
        ApplicationId applicationId,
        string applicationReference,
        TemplateId templateId,
        UserId addedBy,
        DateTime addedOn)
    {
        // Arrange
        var @event = new ContributorAddedEvent(applicationId, applicationReference, templateId, contributor, addedBy, addedOn);
        
        var expectedTemplateId = "3a0e2130-ceea-48c9-8e3d-906431acc86f";
        _emailTemplateResolver.ResolveEmailTemplateAsync(templateId, "ContributorInvited")
            .Returns(expectedTemplateId);

        var failureResponse = new EmailResponse { Id = "test-email-failure-id", Status = EmailStatus.PermanentFailure };
        _emailService.SendEmailAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(failureResponse);

        // Act
        await _handler.Handle(@event, CancellationToken.None);

        // Assert
        _logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Failed to send contributor invitation email")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_Should_Log_Error_And_Not_Throw_When_Exception_Occurs(
        User contributor,
        ApplicationId applicationId,
        string applicationReference,
        TemplateId templateId,
        UserId addedBy,
        DateTime addedOn)
    {
        // Arrange
        var @event = new ContributorAddedEvent(applicationId, applicationReference, templateId, contributor, addedBy, addedOn);
        
        var expectedTemplateId = "3a0e2130-ceea-48c9-8e3d-906431acc86f";
        _emailTemplateResolver.ResolveEmailTemplateAsync(templateId, "ContributorInvited")
            .Returns(expectedTemplateId);

        _emailService.SendEmailAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<EmailResponse>(new Exception("Test exception")));

        // Act & Assert
        var exception = await Record.ExceptionAsync(async () => await _handler.Handle(@event, CancellationToken.None));
        Assert.Null(exception); // Should not throw

        _logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Error sending contributor invitation email")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Theory]
    [CustomAutoData(typeof(UserCustomization))]
    public async Task Handle_Should_Include_Lead_Applicant_Name_In_Baseline_And_Metadata(
        User contributor,
        ApplicationId applicationId,
        string applicationReference,
        TemplateId templateId,
        UserId addedBy,
        DateTime addedOn)
    {
        var leadApplicant = CreateUser(new UserId(Guid.NewGuid()), "Ada Lead", "ada@example.com");
        SetupLeadApplicant(applicationId, leadApplicant);

        var @event = new ContributorAddedEvent(applicationId, applicationReference, templateId, contributor, addedBy, addedOn);

        _emailTemplateResolver.ResolveEmailTemplateAsync(templateId, "ContributorInvited")
            .Returns("3a0e2130-ceea-48c9-8e3d-906431acc86f");
        _emailService.SendEmailAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(new EmailResponse { Id = "test-email-id", Status = EmailStatus.Sent });

        await _handler.Handle(@event, CancellationToken.None);

        await _emailService.Received(1).SendEmailAsync(
            Arg.Is<EmailMessage>(email =>
                email.Personalization[LeadApplicantLookup.BaselinePlaceholderKey].ToString() == "Ada Lead" &&
                email.Personalization["contributor_name"].ToString() == contributor.Name),
            Arg.Any<CancellationToken>());

        await _emailPersonalisationBuilder.Received(1).BuildAsync(
            templateId.Value.ToString(),
            EmailTypes.ContributorInvited,
            applicationId.Value,
            applicationReference,
            Arg.Is<Dictionary<string, object>>(baseline =>
                baseline[LeadApplicantLookup.BaselinePlaceholderKey].ToString() == "Ada Lead"),
            Arg.Any<Dictionary<string, object>>(),
            Arg.Is<IReadOnlyDictionary<string, object?>>(metadata =>
                (string?)metadata[PlatformEventMetadataKeys.LeadApplicantName] == "Ada Lead"),
            Arg.Any<CancellationToken>());
    }

    private void SetupLeadApplicant(ApplicationId applicationId, User leadApplicant)
    {
        var application = new Domain.Entities.Application(
            applicationId,
            "APP-REF",
            new TemplateVersionId(Guid.NewGuid()),
            DateTime.UtcNow,
            leadApplicant.Id!,
            GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums.ApplicationStatus.InProgress);

        _applicationRepository.Query().Returns(new[] { application }.AsQueryable().BuildMock());
        _userRepository.Query().Returns(new[] { leadApplicant }.AsQueryable().BuildMock());
    }

    private static User CreateUser(UserId id, string name, string email) =>
        new(id, new RoleId(Guid.NewGuid()), name, email, DateTime.UtcNow, null, null, null);
}
