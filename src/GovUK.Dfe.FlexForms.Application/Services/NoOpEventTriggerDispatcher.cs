namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Used when MassTransit is not registered (<c>SkipMassTransit</c>), so file upload
/// and submit handlers can still resolve <see cref="IEventTriggerDispatcher"/>.
/// </summary>
public sealed class NoOpEventTriggerDispatcher : IEventTriggerDispatcher
{
    public Task DispatchAsync(
        string triggerName,
        Guid applicationId,
        string applicationReference,
        string templateId,
        Dictionary<string, object> formData,
        IReadOnlyDictionary<string, object?>? platformMetadata = null,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
