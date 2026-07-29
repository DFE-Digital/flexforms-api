using GovUK.Dfe.CoreLibs.Testing.AutoFixture.Attributes;
using GovUK.Dfe.FlexForms.Application.Applications.EventHandlers;
using GovUK.Dfe.FlexForms.Domain.Events;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Tests.Common.Customizations.Entities;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Tests.CommandHandlers.Applications;

public class ApplicationCreatedEventHandlerTests
{
    [Theory]
    [CustomAutoData(typeof(ApplicationCustomization))]
    public async Task Handle_ShouldComplete_WithoutThrowing(
        ApplicationId applicationId,
        UserId userId,
        DateTime createdOn,
        TemplateVersionId tvId,
        string applicationReference,
        ILogger<ApplicationCreatedEventHandler> logger)
    {
        var @event = new ApplicationCreatedEvent(applicationId, applicationReference, tvId, userId, createdOn);
        var handler = new ApplicationCreatedEventHandler(logger);

        await handler.Handle(@event, CancellationToken.None);

        logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Handling event: ApplicationCreatedEvent")),
            null,
            Arg.Any<Func<object, Exception?, string>>());

        logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Event handled successfully: ApplicationCreatedEvent")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }
}
