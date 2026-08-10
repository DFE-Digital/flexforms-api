namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Fans out the tenant-configured outbound events for a lifecycle trigger.
/// Implementations never throw: a publishing failure must not fail the originating
/// submit/upload operation.
/// </summary>
public interface IEventTriggerDispatcher
{
    /// <summary>
    /// Maps and publishes every event configured under <paramref name="triggerName"/>
    /// in the current tenant's EventTriggers settings.
    /// </summary>
    /// <param name="triggerName">Trigger key, see <see cref="Options.EventTriggerType"/>.</param>
    /// <param name="applicationId">Application the trigger fired for.</param>
    /// <param name="applicationReference">Human-readable application reference.</param>
    /// <param name="templateId">Template id used to resolve EventMappings.</param>
    /// <param name="formData">Latest accumulated form data for the application.</param>
    /// <param name="platformMetadata">
    /// Optional platform context for Metadata mappings
    /// (see <see cref="Options.PlatformEventMetadataKeys"/>).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DispatchAsync(
        string triggerName,
        Guid applicationId,
        string applicationReference,
        string templateId,
        Dictionary<string, object> formData,
        IReadOnlyDictionary<string, object?>? platformMetadata = null,
        CancellationToken cancellationToken = default);
}
