using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class ReplicatedTenantConfigurationProviderTests
{
    [Fact]
    public async Task RefreshAsync_RefreshesThisReplica_AndPublishes()
    {
        var replica = new Replica();

        await replica.Provider.RefreshAsync();

        await replica.Inner.Received(1).RefreshAsync(Arg.Any<CancellationToken>());
        Assert.Single(replica.Published);
    }

    [Fact]
    public async Task MessageFromAnotherReplica_RefreshesThisReplica()
    {
        var saving = new Replica();
        var other = new Replica();
        await other.StartAsync();

        await saving.Provider.RefreshAsync();
        other.Deliver(saving.Published.Single());

        await other.Inner.Received(1).RefreshAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OwnMessage_IsIgnored()
    {
        var replica = new Replica();
        await replica.StartAsync();
        await replica.Provider.RefreshAsync();
        replica.Inner.ClearReceivedCalls();

        replica.Deliver(replica.Published.Single());

        await replica.Inner.DidNotReceive().RefreshAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_WhenRedisIsUnavailable_StillRefreshesThisReplica()
    {
        var replica = new Replica(redisAvailable: false);

        await replica.Provider.RefreshAsync();

        await replica.Inner.Received(1).RefreshAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Lookups_AreServedByTheInnerProvider()
    {
        var replica = new Replica();
        var tenantId = Guid.NewGuid();
        var tenant = new TenantConfiguration(
            tenantId, "Visits", new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), []);
        replica.Inner.GetTenant(tenantId).Returns(tenant);
        replica.Inner.Source.Returns("Database");

        Assert.Same(tenant, replica.Provider.GetTenant(tenantId));
        Assert.Equal("Database", replica.Provider.Source);
    }

    private sealed class Replica
    {
        private readonly IServiceProvider _services = Substitute.For<IServiceProvider>();
        private Action<RedisChannel, RedisValue>? _handler;

        public Replica(bool redisAvailable = true)
        {
            var subscriber = Substitute.For<ISubscriber>();
            subscriber
                .PublishAsync(ReplicatedTenantConfigurationProvider.Channel, Arg.Do<RedisValue>(Published.Add), Arg.Any<CommandFlags>())
                .Returns(1L);
            subscriber
                .SubscribeAsync(
                    ReplicatedTenantConfigurationProvider.Channel,
                    Arg.Do<Action<RedisChannel, RedisValue>>(handler =>
                    {
                        _handler = handler;
                        Subscribed.TrySetResult();
                    }),
                    Arg.Any<CommandFlags>())
                .Returns(Task.CompletedTask);

            var redis = Substitute.For<IConnectionMultiplexer>();
            redis.GetSubscriber(Arg.Any<object?>()).Returns(subscriber);
            _services.GetService(typeof(IConnectionMultiplexer)).Returns(_ => redisAvailable
                ? redis
                : throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis is down"));

            Provider = new ReplicatedTenantConfigurationProvider(
                Inner,
                _services,
                NullLogger<ReplicatedTenantConfigurationProvider>.Instance);
        }

        public ITenantConfigurationProvider Inner { get; } = Substitute.For<ITenantConfigurationProvider>();

        public ReplicatedTenantConfigurationProvider Provider { get; }

        public List<RedisValue> Published { get; } = [];

        public TaskCompletionSource Subscribed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task StartAsync()
        {
            await Provider.StartAsync(CancellationToken.None);
            await Subscribed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public void Deliver(RedisValue message) => _handler!(ReplicatedTenantConfigurationProvider.Channel, message);
    }
}
