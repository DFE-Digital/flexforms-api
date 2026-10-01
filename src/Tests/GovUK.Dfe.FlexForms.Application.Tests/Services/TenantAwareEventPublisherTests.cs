using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.CoreLibs.Messaging.MassTransit.Models;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class TenantAwareEventPublisherTests
{
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<TenantAwareEventPublisher> _logger;
    private readonly TenantAwareEventPublisher _publisher;

    public TenantAwareEventPublisherTests()
    {
        _tenantAccessor = Substitute.For<ITenantContextAccessor>();
        _publishEndpoint = Substitute.For<IPublishEndpoint>();
        _logger = Substitute.For<ILogger<TenantAwareEventPublisher>>();
        var endpointSelector = Substitute.For<IMessageEndpointSelector>();
        endpointSelector.GetPublishEndpoint(Arg.Any<Type>()).Returns(_publishEndpoint);
        _publisher = new TenantAwareEventPublisher(endpointSelector, _tenantAccessor, _logger);
    }

    private static TenantConfiguration CreateTenant(Guid? id = null, string name = "TestTenant")
    {
        var settings = new ConfigurationBuilder().Build();
        return new TenantConfiguration(id ?? Guid.NewGuid(), name, settings, Array.Empty<string>());
    }

    [Fact]
    public async Task PublishAsync_ShouldThrow_WhenNoTenantContext()
    {
        _tenantAccessor.CurrentTenant.Returns((TenantConfiguration?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _publisher.PublishAsync(new TestMessage("hello")));
    }

    [Fact]
    public async Task PublishAsync_ShouldDelegate_ToPublishEndpoint()
    {
        var tenant = CreateTenant();
        _tenantAccessor.CurrentTenant.Returns(tenant);

        await _publisher.PublishAsync(new TestMessage("payload"));

        await _publishEndpoint.Received(1).Publish(
            Arg.Is<TestMessage>(m => m.Data == "payload"),
            Arg.Any<IPipe<PublishContext<TestMessage>>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_WithoutProperties_ShouldStillPublish()
    {
        var tenant = CreateTenant();
        _tenantAccessor.CurrentTenant.Returns(tenant);

        await _publisher.PublishAsync(new TestMessage("data"), CancellationToken.None);

        await _publishEndpoint.Received(1).Publish(
            Arg.Any<TestMessage>(),
            Arg.Any<IPipe<PublishContext<TestMessage>>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_WithProperties_ShouldStillPublish()
    {
        var tenant = CreateTenant();
        _tenantAccessor.CurrentTenant.Returns(tenant);

        var properties = new AzureServiceBusMessageProperties();
        properties.CustomProperties["serviceName"] = "extapi-test";

        await _publisher.PublishAsync(new TestMessage("data"), properties, CancellationToken.None);

        await _publishEndpoint.Received(1).Publish(
            Arg.Any<TestMessage>(),
            Arg.Any<IPipe<PublishContext<TestMessage>>>(),
            Arg.Any<CancellationToken>());
    }

    public record TestMessage(string Data);
}
