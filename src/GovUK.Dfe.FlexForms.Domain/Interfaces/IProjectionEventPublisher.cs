using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Domain.Interfaces;

public enum ProjectionTransition
{
    Saved,
    Submitted,
    Deleted
}

/// <summary>
/// An application transition that the Prism analytics projection must hear about.
/// </summary>
public sealed record ProjectionRequest(
    ApplicationId ApplicationId,
    ProjectionTransition Transition,
    long SourceRevision,
    ResponseId? ResponseId,
    long? SubmittedRevision,
    TemplateId? TemplateId,
    TemplateVersionId? TemplateVersionId,
    DateTime OccurredAt);

/// <summary>
/// Publishes Prism projection requests through the transactional outbox.
/// Must be called <b>before</b> the source change is committed so the outbox row commits atomically with it.
/// </summary>
public interface IProjectionEventPublisher
{
    Task PublishAsync(ProjectionRequest request, CancellationToken cancellationToken);
}
