using GovUK.Dfe.FlexForms.Domain.Interfaces;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Used when MassTransit is not registered (e.g. NSwag code generation).
/// </summary>
public sealed class NoOpProjectionEventPublisher : IProjectionEventPublisher
{
    public Task PublishAsync(ProjectionRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
}
