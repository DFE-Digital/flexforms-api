using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Infrastructure.Database.Interceptors;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Messaging;

public class DomainEventDispatcherInterceptorOutboxTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public DomainEventDispatcherInterceptorOutboxTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task SaveChangesAsync_PersistsOutboxMessages_AddedByPostCommitHandlers()
    {
        var mediator = Substitute.For<IMediator>();
        await using var context = CreateContext(mediator);

        mediator
            .When(m => m.Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>()))
            .Do(_ => AddOutboxMessage(context));

        var aggregate = new TestAggregate { Id = Guid.NewGuid() };
        aggregate.Raise(new TestDomainEvent(DateTime.UtcNow));
        context.Aggregates.Add(aggregate);

        await context.SaveChangesAsync();

        await using var verify = CreateContext(Substitute.For<IMediator>());
        Assert.Equal(1, await verify.Set<OutboxMessage>().CountAsync());
        Assert.Equal(1, await verify.Set<OutboxState>().CountAsync());
        Assert.Equal(1, await verify.Aggregates.CountAsync());
    }

    [Fact]
    public async Task SaveChangesAsync_DoesNotWriteOutbox_WhenHandlersPublishNothing()
    {
        var mediator = Substitute.For<IMediator>();
        await using var context = CreateContext(mediator);

        var aggregate = new TestAggregate { Id = Guid.NewGuid() };
        aggregate.Raise(new TestDomainEvent(DateTime.UtcNow));
        context.Aggregates.Add(aggregate);

        var saved = await context.SaveChangesAsync();

        Assert.Equal(1, saved);
        await mediator.Received(1).Publish(Arg.Any<IDomainEvent>(), Arg.Any<CancellationToken>());
        Assert.Equal(0, await context.Set<OutboxMessage>().CountAsync());
    }

    private TestDbContext CreateContext(IMediator mediator)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new DomainEventDispatcherInterceptor(mediator))
            .Options;

        var context = new TestDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private static void AddOutboxMessage(DbContext context)
    {
        var outboxId = Guid.NewGuid();
        context.Add(new OutboxState { OutboxId = outboxId, Created = DateTime.UtcNow });
        context.Add(new OutboxMessage
        {
            OutboxId = outboxId,
            MessageId = Guid.NewGuid(),
            SentTime = DateTime.UtcNow,
            ContentType = "application/vnd.masstransit+json",
            MessageType = "urn:message:Test:TestEvent",
            Body = "{}"
        });
    }

    private sealed record TestDomainEvent(DateTime OccurredOn) : IDomainEvent;

    private sealed class TestAggregate : IHasDomainEvents
    {
        private readonly List<IDomainEvent> _events = [];

        public Guid Id { get; set; }

        public IReadOnlyCollection<IDomainEvent> DomainEvents => _events;

        public void Raise(IDomainEvent @event) => _events.Add(@event);

        public void ClearDomainEvents() => _events.Clear();
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<TestAggregate> Aggregates => Set<TestAggregate>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestAggregate>(b =>
            {
                b.HasKey(x => x.Id);
                b.Ignore(x => x.DomainEvents);
            });

            modelBuilder.AddInboxStateEntity();
            modelBuilder.AddOutboxMessageEntity();
            modelBuilder.AddOutboxStateEntity();
        }
    }
}
