using System.Collections.Concurrent;
using System.Data.Common;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Enums;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Events;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Identifiers;
using GovUK.Dfe.FlexForms.Api.Tenancy;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Factories;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using GovUK.Dfe.FlexForms.Infrastructure;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Infrastructure.Repositories;
using GovUK.Dfe.FlexForms.Tests.Common.Seeders;
using GovUK.Dfe.FlexForms.Utils.Configuration;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Api.Tests.Messaging.Outbox;

/// <summary>
/// Proves the Prism guarantee on real SQL Server with the production outbox registration: the data change, the
/// <c>SourceRevision</c> increment and the outbox row commit or roll back together, an outage of the bus loses
/// nothing, and a message that is sent again keeps its <c>MessageId</c>.
/// </summary>
[Collection(SqlServerOutboxCollection.Name)]
public sealed class PrismOutboxAcceptanceTests(SqlServerOutboxFixture sql, ITestOutputHelper output)
{
    private static readonly ApplicationId App = new(Guid.Parse(EaContextSeeder.ApplicationId));
    private static readonly UserId Alice = new(Guid.Parse(EaContextSeeder.AliceId));
    private static readonly Guid TenantId = Guid.Parse(EaContextSeeder.TestTenantId);

    [Fact]
    public async Task A_save_whose_transaction_rolls_back_leaves_no_data_no_revision_and_no_outbox_row()
    {
        await using var host = await OutboxHost.CreateAsync(sql, output);
        var before = await host.ReadAsync();

        host.FailCommits = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.SaveResponseAsync("{\"name\":\"rolled back\"}"));

        var after = await host.ReadAsync();
        Assert.Equal(before, after);
        Assert.Equal(0, after.OutboxMessages);
        await host.StartBusAndDeliveryAsync();
        Assert.False(await host.Harness.Consumed.Any<ApplicationProjectionRequestedEvent>());
        Assert.Empty(await host.WaitForDeliveriesAsync(0));
    }

    [Fact]
    public async Task A_delete_whose_transaction_rolls_back_leaves_no_data_no_revision_and_no_outbox_row()
    {
        await using var host = await OutboxHost.CreateAsync(sql, output);
        var before = await host.ReadAsync();

        host.FailCommits = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.DeleteAsync());

        var after = await host.ReadAsync();
        Assert.Equal(before, after);
        Assert.NotEqual(ApplicationStatus.Deleted, after.Status);
        Assert.Equal(0, after.OutboxMessages);
    }

    [Fact]
    public async Task A_save_committed_while_the_bus_is_down_is_kept_and_delivered_when_it_recovers()
    {
        await using var host = await OutboxHost.CreateAsync(sql, output);
        var before = await host.ReadAsync();

        await host.SaveResponseAsync("{\"name\":\"committed\"}");

        var committed = await host.ReadAsync();
        Assert.Equal(before.SourceRevision + 1, committed.SourceRevision);
        Assert.Equal(before.Responses + 1, committed.Responses);
        Assert.Equal(1, committed.OutboxMessages);
        var stored = await host.SingleOutboxMessageAsync();
        var expectedId = ApplicationProjectionIdentifiers.MessageId(TenantId, App.Value, committed.SourceRevision, ProjectionReason.Saved);
        Assert.Equal(expectedId, stored.MessageId);

        await host.StartBusAndDeliveryAsync();

        var delivered = Assert.Single(await host.WaitForDeliveriesAsync(1));
        Assert.Equal(expectedId, delivered.MessageId);
        Assert.Equal((ProjectionReason.Saved, committed.SourceRevision, TenantId), (delivered.Message.Reason, delivered.Message.SourceRevision, delivered.Message.TenantId));
        await host.WaitForOutboxToDrainAsync();
    }

    [Fact]
    public async Task A_delete_commits_its_outbox_row_with_the_revision_it_produced()
    {
        await using var host = await OutboxHost.CreateAsync(sql, output);
        var before = await host.ReadAsync();

        await host.DeleteAsync();

        var after = await host.ReadAsync();
        Assert.Equal(ApplicationStatus.Deleted, after.Status);
        Assert.Equal(before.SourceRevision + 1, after.SourceRevision);
        var stored = await host.SingleOutboxMessageAsync();
        Assert.Equal(ApplicationProjectionIdentifiers.MessageId(TenantId, App.Value, after.SourceRevision, ProjectionReason.Deleted), stored.MessageId);
    }

    [Fact]
    public async Task A_message_sent_again_after_a_lost_acknowledgement_keeps_its_MessageId()
    {
        await using var host = await OutboxHost.CreateAsync(sql, output);
        await host.SaveResponseAsync("{\"name\":\"sent twice\"}");
        var stored = await host.SingleOutboxMessageAsync();

        await host.StartBusAndDeliveryAsync();
        await host.WaitForDeliveriesAsync(1);
        await host.WaitForOutboxToDrainAsync();

        // The send succeeded but the row was never marked delivered, so delivery sends the same row again.
        await host.RestoreOutboxMessageAsync(stored);

        var deliveries = await host.WaitForDeliveriesAsync(2);
        Assert.Equal(2, deliveries.Count);
        Assert.All(deliveries, d => Assert.Equal(stored.MessageId, d.MessageId));
        Assert.Equal(deliveries[0].Message, deliveries[1].Message);
    }

    [Fact]
    public async Task A_new_template_version_whose_transaction_rolls_back_leaves_no_version_and_no_outbox_row()
    {
        await using var host = await OutboxHost.CreateAsync(sql, output);
        var versionsBefore = await host.CountTemplateVersionsAsync();

        host.FailCommits = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.CreateTemplateVersionAsync("9.9"));

        Assert.Equal(versionsBefore, await host.CountTemplateVersionsAsync());
        Assert.Equal(0, (await host.ReadAsync()).OutboxMessages);
    }

    [Fact]
    public async Task A_new_template_version_committed_while_the_bus_is_down_is_delivered_when_it_recovers()
    {
        await using var host = await OutboxHost.CreateAsync(sql, output);

        var versionId = await host.CreateTemplateVersionAsync("2.0");

        var stored = await host.SingleOutboxMessageAsync();
        var expectedId = ApplicationProjectionIdentifiers.TemplateVersionMessageId(TenantId, versionId);
        Assert.Equal(expectedId, stored.MessageId);

        await host.StartBusAndDeliveryAsync();

        var delivered = Assert.Single(await host.WaitForTemplateDeliveriesAsync(1));
        Assert.Equal(expectedId, delivered.MessageId);
        Assert.Equal(
            (TenantId, Guid.Parse(EaContextSeeder.TemplateId), versionId, "2.0"),
            (delivered.Message.TenantId, delivered.Message.TemplateId, delivered.Message.TemplateVersionId, delivered.Message.VersionNumber));
        await host.WaitForOutboxToDrainAsync();
    }

    private sealed record SourceState(long SourceRevision, ApplicationStatus? Status, int Responses, int OutboxMessages);

    /// <summary>
    /// The API's messaging and persistence wiring for one tenant: the real repository, publisher, endpoint
    /// selector and <c>AddFlexFormsTransactionalOutbox</c>, over the MassTransit test harness.
    /// </summary>
    private sealed class OutboxHost : IAsyncDisposable
    {
        private readonly CommitFailure _commitFailure = new();
        private readonly ConcurrentQueue<ConsumeContext<ApplicationProjectionRequestedEvent>> _received = new();
        private readonly ConcurrentQueue<ConsumeContext<TemplateVersionPublishedEvent>> _templatesReceived = new();
        private readonly ServiceProvider _provider;
        private readonly TenantConfiguration _tenant;

        private OutboxHost(string connectionString, ITestOutputHelper output)
        {
            var tenantSettings = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connectionString })
                .Build();
            _tenant = new TenantConfiguration(TenantId, "Test tenant", tenantSettings, []);
            var tenants = Substitute.For<ITenantConfigurationProvider>();
            tenants.GetAllTenants().Returns([_tenant]);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MassTransit:Outbox:Enabled"] = "true",
                    ["MassTransit:Outbox:Delivery:QueryDelay"] = "00:00:00.200",
                })
                .Build();

            var services = new ServiceCollection()
                .AddLogging(logging => logging
                    .AddProvider(new TestOutputLoggerProvider(output))
                    .AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning))
                .AddSingleton<IConfiguration>(configuration)
                .AddSingleton(tenants)
                .AddSingleton(_received)
                .AddSingleton(_templatesReceived)
                .AddScoped<ITenantContextAccessor, TenantContextAccessor>()
                .AddDbContext<ExternalApplicationsContext>(o => o.UseSqlServer(connectionString).AddInterceptors(_commitFailure))
                .AddScoped<IApplicationRepository, ApplicationRepository>()
                .AddScoped<IMessageEndpointSelector, MessageEndpointSelector>()
                .AddScoped<IProjectionEventPublisher, ProjectionEventPublisher>();
            services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
            services.AddMassTransitTestHarness(bus =>
            {
                bus.AddConsumer<ProjectionProbe>();
                bus.AddConsumer<TemplateVersionProbe>();
                bus.AddFlexFormsTransactionalOutbox(configuration);
            });

            _provider = services.BuildServiceProvider();
            Harness = _provider.GetRequiredService<ITestHarness>();
        }

        public ITestHarness Harness { get; }

        public bool FailCommits
        {
            set => _commitFailure.Enabled = value;
        }

        public static async Task<OutboxHost> CreateAsync(SqlServerOutboxFixture sql, ITestOutputHelper output)
            => new(await sql.CreateDatabaseAsync(), output);

        /// <summary>The same calls, in the same order, as <c>AddApplicationResponseCommandHandler</c>.</summary>
        public Task SaveResponseAsync(string body) => InTenantScopeAsync(async services =>
        {
            var publisher = services.GetRequiredService<IProjectionEventPublisher>();
            var append = new ApplicationResponseAppender().Create(App, body, Alice);
            await services.GetRequiredService<IApplicationRepository>().AppendResponseVersionAsync(
                App,
                append.Response,
                append.Now,
                Alice,
                CancellationToken.None,
                (appended, ct) => publisher.PublishAsync(
                    new ProjectionRequest(
                        appended.ApplicationId,
                        ProjectionTransition.Saved,
                        appended.SourceRevision,
                        appended.ResponseId,
                        SubmittedRevision: null,
                        appended.TemplateId,
                        appended.TemplateVersionId,
                        append.Now),
                    ct));
        });

        /// <summary>The same calls, in the same order, as <c>DeleteApplicationCommandHandler</c>.</summary>
        public Task DeleteAsync() => InTenantScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ExternalApplicationsContext>();
            var application = await db.Applications.Include(a => a.TemplateVersion).SingleAsync(a => a.Id == App);
            var now = DateTime.UtcNow;
            application.Delete(now, Alice, "alice@example.com", "Alice Anderson");

            await services.GetRequiredService<IProjectionEventPublisher>().PublishAsync(
                new ProjectionRequest(
                    App,
                    ProjectionTransition.Deleted,
                    application.SourceRevision,
                    ResponseId: null,
                    application.SubmittedRevision,
                    application.TemplateVersion!.TemplateId,
                    application.TemplateVersionId,
                    now),
                CancellationToken.None);

            await new UnitOfWork(db).CommitAsync();
        });

        /// <summary>The same calls, in the same order, as <c>CreateTemplateVersionCommandHandler</c>.</summary>
        public async Task<Guid> CreateTemplateVersionAsync(string versionNumber)
        {
            Guid versionId = default;
            await InTenantScopeAsync(async services =>
            {
                var db = services.GetRequiredService<ExternalApplicationsContext>();
                var templateId = new TemplateId(Guid.Parse(EaContextSeeder.TemplateId));
                var template = await db.Templates.Include(t => t.TemplateVersions).SingleAsync(t => t.Id == templateId);
                var version = new TemplateFactory().AddVersionToTemplate(template, versionNumber, "{\"pages\":[]}", Alice);
                versionId = version.Id!.Value;

                await services.GetRequiredService<IProjectionEventPublisher>().PublishTemplateVersionAsync(
                    new TemplateVersionPublication(template.Id!, version.Id!, version.VersionNumber, version.CreatedOn),
                    CancellationToken.None);

                await new UnitOfWork(db).CommitAsync();
            });
            return versionId;
        }

        public async Task<int> CountTemplateVersionsAsync()
        {
            await using var scope = _provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ExternalApplicationsContext>().TemplateVersions.CountAsync();
        }

        public async Task<SourceState> ReadAsync()
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ExternalApplicationsContext>();
            var application = await db.Applications.AsNoTracking().SingleAsync(a => a.Id == App);
            return new SourceState(
                application.SourceRevision,
                application.Status,
                await db.ApplicationResponses.CountAsync(r => r.ApplicationId == App),
                await db.Set<OutboxMessage>().CountAsync());
        }

        public async Task<OutboxMessage> SingleOutboxMessageAsync()
        {
            await using var scope = _provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ExternalApplicationsContext>()
                .Set<OutboxMessage>().AsNoTracking().SingleAsync();
        }

        /// <summary>Puts a delivered row back, as if the delivery loop crashed before recording the send.</summary>
        public async Task RestoreOutboxMessageAsync(OutboxMessage delivered)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ExternalApplicationsContext>();
            var outboxId = Guid.NewGuid();
            db.Add(new OutboxState { OutboxId = outboxId, Created = DateTime.UtcNow });
            db.Add(new OutboxMessage
            {
                OutboxId = outboxId,
                MessageId = delivered.MessageId,
                ConversationId = delivered.ConversationId,
                CorrelationId = delivered.CorrelationId,
                InitiatorId = delivered.InitiatorId,
                SourceAddress = delivered.SourceAddress,
                DestinationAddress = delivered.DestinationAddress,
                MessageType = delivered.MessageType,
                ContentType = delivered.ContentType,
                Body = delivered.Body,
                Headers = delivered.Headers,
                Properties = delivered.Properties,
                SentTime = delivered.SentTime,
            });
            await db.SaveChangesAsync();
        }

        /// <summary>Starts the bus and the tenant-aware delivery loop, which were down until now.</summary>
        public async Task StartBusAndDeliveryAsync()
        {
            await Harness.Start();
        }

        /// <summary>Waits until Prism's side of the bus has received <paramref name="count"/> projection events.</summary>
        public Task<IReadOnlyList<ConsumeContext<ApplicationProjectionRequestedEvent>>> WaitForDeliveriesAsync(int count)
            => WaitForAsync(_received, count);

        public Task<IReadOnlyList<ConsumeContext<TemplateVersionPublishedEvent>>> WaitForTemplateDeliveriesAsync(int count)
            => WaitForAsync(_templatesReceived, count);

        private static async Task<IReadOnlyList<ConsumeContext<T>>> WaitForAsync<T>(ConcurrentQueue<ConsumeContext<T>> queue, int count)
            where T : class
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (true)
            {
                var received = queue.ToList();
                if (received.Count >= count)
                    return received;

                Assert.True(DateTime.UtcNow < deadline, $"Received {received.Count} of {count} {typeof(T).Name} messages within 30 seconds.");
                await Task.Delay(200);
            }
        }

        public async Task WaitForOutboxToDrainAsync()
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while ((await ReadAsync()).OutboxMessages > 0)
            {
                Assert.True(DateTime.UtcNow < deadline, "The outbox was not delivered within 30 seconds.");
                await Task.Delay(200);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Harness.Stop();
            await _provider.DisposeAsync();
        }

        private async Task InTenantScopeAsync(Func<IServiceProvider, Task> work)
        {
            await using var scope = _provider.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().CurrentTenant = _tenant;
            await work(scope.ServiceProvider);
        }
    }

    /// <summary>Records every delivery; the harness's own message lists collapse deliveries with the same MessageId.</summary>
    private sealed class ProjectionProbe(ConcurrentQueue<ConsumeContext<ApplicationProjectionRequestedEvent>> received)
        : IConsumer<ApplicationProjectionRequestedEvent>
    {
        public Task Consume(ConsumeContext<ApplicationProjectionRequestedEvent> context)
        {
            received.Enqueue(context);
            return Task.CompletedTask;
        }
    }

    private sealed class TemplateVersionProbe(ConcurrentQueue<ConsumeContext<TemplateVersionPublishedEvent>> received)
        : IConsumer<TemplateVersionPublishedEvent>
    {
        public Task Consume(ConsumeContext<TemplateVersionPublishedEvent> context)
        {
            received.Enqueue(context);
            return Task.CompletedTask;
        }
    }

    private sealed class TestOutputLoggerProvider(ITestOutputHelper output) : ILoggerProvider, ILogger
    {
        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            try
            {
                output.WriteLine($"{logLevel}: {formatter(state, exception)}{(exception is null ? "" : Environment.NewLine + exception)}");
            }
            catch (InvalidOperationException)
            {
                // The test has finished; background services can still log while stopping.
            }
        }

        public void Dispose()
        {
        }
    }

    /// <summary>Fails the commit of every transaction while enabled, after all statements have run.</summary>
    private sealed class CommitFailure : DbTransactionInterceptor
    {
        public bool Enabled { get; set; }

        public override InterceptionResult TransactionCommitting(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
            => Enabled ? throw new InvalidOperationException("Simulated failure before commit.") : result;

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
            => Enabled ? throw new InvalidOperationException("Simulated failure before commit.") : ValueTask.FromResult(result);
    }
}
