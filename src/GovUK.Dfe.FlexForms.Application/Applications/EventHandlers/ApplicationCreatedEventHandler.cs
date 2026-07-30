using GovUK.Dfe.FlexForms.Application.Common.EventHandlers;
using GovUK.Dfe.FlexForms.Domain.Events;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.Applications.EventHandlers;

/// <summary>
/// Side-effects for <see cref="ApplicationCreatedEvent"/>.
/// Creator Application/ApplicationFiles permissions are granted in
/// <c>CreateApplicationCommandHandler</c> on the User aggregate before commit
/// (so write access is available immediately after create).
/// </summary>
public sealed class ApplicationCreatedEventHandler(
    ILogger<ApplicationCreatedEventHandler> logger)
    : BaseEventHandler<ApplicationCreatedEvent>(logger)
{
    protected override Task HandleEvent(ApplicationCreatedEvent notification, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Application {ApplicationId} ({ApplicationReference}) created by {CreatedBy}",
            notification.ApplicationId.Value,
            notification.ApplicationReference,
            notification.CreatedBy.Value);

        return Task.CompletedTask;
    }
}
