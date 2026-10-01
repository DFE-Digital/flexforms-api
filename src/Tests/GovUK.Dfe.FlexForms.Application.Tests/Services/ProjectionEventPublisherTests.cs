using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Enums;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Events;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Identifiers;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class ProjectionEventPublisherTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private readonly IPublishEndpoint _publishEndpoint = Substitute.For<IPublishEndpoint>();
    private readonly IMessageEndpointSelector _selector = Substitute.For<IMessageEndpointSelector>();
    private readonly ITenantContextAccessor _tenantAccessor = Substitute.For<ITenantContextAccessor>();
    private readonly ProjectionEventPublisher _publisher;

    private ApplicationProjectionRequestedEvent? _published;
    private IPipe<PublishContext<ApplicationProjectionRequestedEvent>>? _pipe;

    public ProjectionEventPublisherTests()
    {
        _selector.GetPublishEndpoint(typeof(ApplicationProjectionRequestedEvent)).Returns(_publishEndpoint);
        _tenantAccessor.CurrentTenant.Returns(
            new TenantConfiguration(TenantId, "Tenant A", new ConfigurationBuilder().Build(), Array.Empty<string>()));

        _publishEndpoint
            .Publish(
                Arg.Do<ApplicationProjectionRequestedEvent>(m => _published = m),
                Arg.Do<IPipe<PublishContext<ApplicationProjectionRequestedEvent>>>(p => _pipe = p),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _publisher = new ProjectionEventPublisher(_selector, _tenantAccessor, Substitute.For<ILogger<ProjectionEventPublisher>>());
    }

    private static ProjectionRequest Request(
        ProjectionTransition transition,
        long revision = 4,
        ResponseId? responseId = null,
        long? submittedRevision = null) => new(
            new ApplicationId(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002")),
            transition,
            revision,
            responseId ?? new ResponseId(Guid.NewGuid()),
            submittedRevision,
            new TemplateId(Guid.NewGuid()),
            new TemplateVersionId(Guid.NewGuid()),
            DateTime.UtcNow);

    [Fact]
    public async Task Saved_publishes_contract_with_application_revision()
    {
        var request = Request(ProjectionTransition.Saved);

        await _publisher.PublishAsync(request, CancellationToken.None);

        Assert.NotNull(_published);
        Assert.Equal(ApplicationProjectionRequestedEvent.CurrentContractVersion, _published!.ContractVersion);
        Assert.Equal(TenantId, _published.TenantId);
        Assert.Equal(request.ApplicationId.Value, _published.ApplicationId);
        Assert.Equal(ProjectionReason.Saved, _published.Reason);
        Assert.Equal(4, _published.SourceRevision);
        Assert.Equal(request.ResponseId!.Value, _published.ResponseId);
        Assert.Null(_published.SubmissionId);
        Assert.Null(_published.OperationId);
    }

    [Fact]
    public async Task Submitted_includes_deterministic_submission_id()
    {
        var request = Request(ProjectionTransition.Submitted, revision: 6, submittedRevision: 6);

        await _publisher.PublishAsync(request, CancellationToken.None);

        Assert.Equal(ProjectionReason.Submitted, _published!.Reason);
        Assert.Equal(ApplicationProjectionIdentifiers.SubmissionId(request.ApplicationId.Value, 6), _published.SubmissionId);
    }

    [Fact]
    public async Task Deleted_does_not_require_a_response()
    {
        var request = Request(ProjectionTransition.Deleted) with { ResponseId = null };

        await _publisher.PublishAsync(request, CancellationToken.None);

        Assert.Equal(ProjectionReason.Deleted, _published!.Reason);
        Assert.Null(_published.ResponseId);
    }

    [Theory]
    [InlineData(ProjectionTransition.Saved)]
    [InlineData(ProjectionTransition.Submitted)]
    public async Task Saved_and_submitted_require_a_response(ProjectionTransition transition)
    {
        var request = Request(transition) with { ResponseId = null };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _publisher.PublishAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task Throws_without_tenant_context()
    {
        _tenantAccessor.CurrentTenant.Returns((TenantConfiguration?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _publisher.PublishAsync(Request(ProjectionTransition.Saved), CancellationToken.None));
    }

    [Fact]
    public async Task Stamps_deterministic_message_id_and_tenant_headers()
    {
        var request = Request(ProjectionTransition.Saved, revision: 9);
        await _publisher.PublishAsync(request, CancellationToken.None);

        var context = Substitute.For<PublishContext<ApplicationProjectionRequestedEvent>>();
        var headers = Substitute.For<SendHeaders>();
        context.Headers.Returns(headers);

        await _pipe!.Send(context);

        context.Received().MessageId = ApplicationProjectionIdentifiers.MessageId(
            TenantId, request.ApplicationId.Value, 9, ProjectionReason.Saved);
        headers.Received().Set("TenantId", TenantId.ToString());
        headers.Received().Set("TenantName", "Tenant A");
    }

    [Fact]
    public async Task Template_version_publishes_contract_with_deterministic_id_and_tenant_headers()
    {
        var templateEndpoint = Substitute.For<IPublishEndpoint>();
        _selector.GetPublishEndpoint(typeof(TemplateVersionPublishedEvent)).Returns(templateEndpoint);
        TemplateVersionPublishedEvent? published = null;
        IPipe<PublishContext<TemplateVersionPublishedEvent>>? pipe = null;
        templateEndpoint
            .Publish(
                Arg.Do<TemplateVersionPublishedEvent>(m => published = m),
                Arg.Do<IPipe<PublishContext<TemplateVersionPublishedEvent>>>(p => pipe = p),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var publication = new TemplateVersionPublication(
            new TemplateId(Guid.NewGuid()), new TemplateVersionId(Guid.NewGuid()), "2.1", new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc));

        await _publisher.PublishTemplateVersionAsync(publication, CancellationToken.None);

        Assert.Equal(
            new TemplateVersionPublishedEvent(
                TemplateVersionPublishedEvent.CurrentContractVersion,
                TenantId,
                publication.TemplateId.Value,
                publication.TemplateVersionId.Value,
                "2.1",
                publication.CreatedAt),
            published);

        var context = Substitute.For<PublishContext<TemplateVersionPublishedEvent>>();
        var headers = Substitute.For<SendHeaders>();
        context.Headers.Returns(headers);
        await pipe!.Send(context);

        context.Received().MessageId = ApplicationProjectionIdentifiers.TemplateVersionMessageId(TenantId, publication.TemplateVersionId.Value);
        headers.Received().Set("TenantId", TenantId.ToString());
        headers.Received().Set("TenantName", "Tenant A");
    }

    [Fact]
    public async Task Template_version_throws_without_tenant_context()
    {
        _tenantAccessor.CurrentTenant.Returns((TenantConfiguration?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _publisher.PublishTemplateVersionAsync(
            new TemplateVersionPublication(new TemplateId(Guid.NewGuid()), new TemplateVersionId(Guid.NewGuid()), "1", DateTime.UtcNow),
            CancellationToken.None));
    }

    [Fact]
    public async Task Same_transition_published_twice_gets_the_same_message_id()
    {
        var request = Request(ProjectionTransition.Saved, revision: 3);
        var ids = new List<Guid?>();

        for (var i = 0; i < 2; i++)
        {
            await _publisher.PublishAsync(request, CancellationToken.None);
            var context = Substitute.For<PublishContext<ApplicationProjectionRequestedEvent>>();
            context.Headers.Returns(Substitute.For<SendHeaders>());
            context.When(c => c.MessageId = Arg.Any<Guid?>()).Do(ci => ids.Add(ci.Arg<Guid?>()));
            await _pipe!.Send(context);
        }

        Assert.Equal(2, ids.Count);
        Assert.Equal(ids[0], ids[1]);
    }
}
