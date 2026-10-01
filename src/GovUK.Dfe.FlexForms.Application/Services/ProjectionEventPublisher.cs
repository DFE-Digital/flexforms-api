using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Enums;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Events;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Identifiers;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Publishes <see cref="ApplicationProjectionRequestedEvent"/> for Prism. Always routed through the
/// scoped (outbox) endpoint, with a deterministic message id so Service Bus duplicate detection
/// can drop redeliveries. The session id is applied by the send topology at delivery time.
/// </summary>
public sealed class ProjectionEventPublisher(
    IMessageEndpointSelector endpointSelector,
    ITenantContextAccessor tenantAccessor,
    ILogger<ProjectionEventPublisher> logger) : IProjectionEventPublisher
{
    private const string TenantIdHeader = "TenantId";
    private const string TenantNameHeader = "TenantName";

    public Task PublishAsync(ProjectionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tenant = tenantAccessor.CurrentTenant
            ?? throw new InvalidOperationException("Tenant context is required to publish projection events.");

        var reason = request.Transition switch
        {
            ProjectionTransition.Saved => ProjectionReason.Saved,
            ProjectionTransition.Submitted => ProjectionReason.Submitted,
            ProjectionTransition.Deleted => ProjectionReason.Deleted,
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Transition, "Unknown projection transition")
        };

        if ((reason is ProjectionReason.Saved or ProjectionReason.Submitted) && request.ResponseId is null)
            throw new InvalidOperationException($"{reason} projection requests require a response id.");

        var applicationId = request.ApplicationId.Value;
        Guid? submissionId = reason == ProjectionReason.Submitted
            ? ApplicationProjectionIdentifiers.SubmissionId(applicationId, request.SubmittedRevision ?? request.SourceRevision)
            : null;

        var message = new ApplicationProjectionRequestedEvent(
            ApplicationProjectionRequestedEvent.CurrentContractVersion,
            tenant.Id,
            applicationId,
            reason,
            request.SourceRevision,
            request.ResponseId?.Value,
            submissionId,
            request.TemplateId?.Value,
            request.TemplateVersionId?.Value,
            OperationId: null,
            request.OccurredAt);

        var messageId = ApplicationProjectionIdentifiers.MessageId(tenant.Id, applicationId, request.SourceRevision, reason);

        logger.LogDebug(
            "Publishing Prism projection request {Reason} for application {ApplicationId} revision {SourceRevision} (tenant {TenantId}, message {MessageId})",
            reason, applicationId, request.SourceRevision, tenant.Id, messageId);

        return endpointSelector.GetPublishEndpoint(typeof(ApplicationProjectionRequestedEvent)).Publish(message, ctx =>
        {
            ctx.MessageId = messageId;
            ctx.Headers.Set(TenantIdHeader, tenant.Id.ToString());
            ctx.Headers.Set(TenantNameHeader, tenant.Name);
        }, cancellationToken);
    }
}
