using GovUK.Dfe.FlexForms.Api.Tests.Messaging.Outbox;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Security;

/// <summary>
/// The cross-replica guarantees of <see cref="TestAuthAuthenticatorStore"/> against real SQL Server: the unique
/// (TenantId, NormalizedEmail) index and the <c>SetupId</c> concurrency token. Each "replica" is a store with its own
/// DbContext. <see cref="BeforeSaveInterceptor"/> runs the other replica's whole operation at the exact point before
/// this replica writes, so every conflict path is hit deterministically.
/// </summary>
[Collection(SqlServerOutboxCollection.Name)]
public sealed class TestAuthAuthenticatorStoreSqlServerTests(SqlServerOutboxFixture sql) : IAsyncLifetime
{
    private const string Email = "tester@education.gov.uk";
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc);

    private readonly EphemeralDataProtectionProvider _dataProtection = new();
    private readonly List<IAsyncDisposable> _contexts = [];
    private string _connectionString = string.Empty;

    public async Task InitializeAsync() => _connectionString = await sql.CreateTenantConfigDatabaseAsync(TenantId);

    public async Task DisposeAsync()
    {
        foreach (var context in _contexts)
            await context.DisposeAsync();
    }

    [Fact]
    public async Task Overlapping_first_time_saves_leave_exactly_one_enrolment()
    {
        var (a, aInterceptor) = Replica();
        var (b, _) = Replica();
        aInterceptor.BeforeNextSave = async () =>
            Assert.True(await b.SaveAsync(TenantId, Email, [2], Now, Setup(2), expectedCurrentSetupId: null));

        // A read "no enrolment", then B inserted before A wrote: A must lose on the unique index, not throw.
        Assert.False(await a.SaveAsync(TenantId, Email, [1], Now, Setup(1), expectedCurrentSetupId: null));

        await AssertCurrentAsync(Setup(2), secret: [2]);
    }

    [Fact]
    public async Task Many_parallel_first_time_saves_leave_exactly_one_enrolment()
    {
        var replicas = Enumerable.Range(1, 6).Select(_ => Replica().Store).ToList();

        var results = await Task.WhenAll(replicas.Select((store, i) =>
            store.SaveAsync(TenantId, Email, [(byte)i], Now, Setup(i + 1), expectedCurrentSetupId: null)));

        Assert.Single(results, saved => saved);
        Assert.Equal(1, await CountRowsAsync());
    }

    [Fact]
    public async Task Save_loses_to_a_removal_that_commits_first()
    {
        await Seed(Setup(1));
        var (a, aInterceptor) = Replica();
        var (b, _) = Replica();
        aInterceptor.BeforeNextSave = async () => Assert.True(await b.DeleteAsync(TenantId, Email, Now));

        // A expected generation 1; B's removal moved it on, so A's conditional UPDATE matches no row.
        Assert.False(await a.SaveAsync(TenantId, Email, [2], Now, Setup(2), Setup(1)));

        Assert.Null(await Reader().FindAsync(TenantId, Email));
        Assert.NotEqual(Setup(1), await Reader().GetCurrentSetupIdAsync(TenantId, Email));
    }

    [Fact]
    public async Task Save_loses_to_a_newer_save_of_the_same_generation()
    {
        await Seed(Setup(1));
        var (a, aInterceptor) = Replica();
        var (b, _) = Replica();
        aInterceptor.BeforeNextSave = async () => Assert.True(await b.SaveAsync(TenantId, Email, [3], Now, Setup(3), Setup(1)));

        Assert.False(await a.SaveAsync(TenantId, Email, [2], Now, Setup(2), Setup(1)));

        await AssertCurrentAsync(Setup(3), secret: [3]);
    }

    [Fact]
    public async Task Removal_retries_and_removes_an_enrolment_saved_while_it_was_removing()
    {
        await Seed(Setup(1));
        var (a, aInterceptor) = Replica();
        var (b, _) = Replica();
        aInterceptor.BeforeNextSave = async () => Assert.True(await b.SaveAsync(TenantId, Email, [2], Now, Setup(2), Setup(1)));

        // A read generation 1; B replaced it; A's first write conflicts, so A re-reads and removes generation 2.
        Assert.True(await a.DeleteAsync(TenantId, Email, Now));

        Assert.Null(await Reader().FindAsync(TenantId, Email));
        Assert.Equal(1, await CountRowsAsync());
    }

    [Fact]
    public async Task Removal_without_enrolment_retries_when_a_first_time_save_inserts_first()
    {
        var (a, aInterceptor) = Replica();
        var (b, _) = Replica();
        aInterceptor.BeforeNextSave = async () =>
            Assert.True(await b.SaveAsync(TenantId, Email, [2], Now, Setup(2), expectedCurrentSetupId: null));

        // A was inserting a tombstone; B's insert won the unique index; A retries and removes B's enrolment.
        Assert.True(await a.DeleteAsync(TenantId, Email, Now));

        Assert.Null(await Reader().FindAsync(TenantId, Email));
        Assert.Equal(1, await CountRowsAsync());
    }

    [Fact]
    public async Task First_time_save_loses_to_a_removal_that_inserted_a_tombstone_first()
    {
        var (a, aInterceptor) = Replica();
        var (b, _) = Replica();
        aInterceptor.BeforeNextSave = async () => Assert.False(await b.DeleteAsync(TenantId, Email, Now));

        Assert.False(await a.SaveAsync(TenantId, Email, [1], Now, Setup(1), expectedCurrentSetupId: null));

        Assert.Null(await Reader().FindAsync(TenantId, Email));
        Assert.Equal(1, await CountRowsAsync());
    }

    private (TestAuthAuthenticatorStore Store, BeforeSaveInterceptor Interceptor) Replica()
    {
        var interceptor = new BeforeSaveInterceptor();
        var context = new TenantConfigDbContext(new DbContextOptionsBuilder<TenantConfigDbContext>()
            .UseSqlServer(_connectionString)
            .AddInterceptors(interceptor)
            .Options);
        _contexts.Add(context);

        var store = new TestAuthAuthenticatorStore(
            context,
            new DataProtectionTestAuthAuthenticatorSecretProtector(_dataProtection),
            NullLogger<TestAuthAuthenticatorStore>.Instance);
        return (store, interceptor);
    }

    private TestAuthAuthenticatorStore Reader() => Replica().Store;

    private Task Seed(string setupId)
        => Reader().SaveAsync(TenantId, Email, [1], Now, setupId, expectedCurrentSetupId: null);

    private async Task AssertCurrentAsync(string setupId, byte[] secret)
    {
        var reader = Reader();
        Assert.Equal(setupId, await reader.GetCurrentSetupIdAsync(TenantId, Email));
        Assert.Equal(secret, (await reader.FindAsync(TenantId, Email))!.Secret);
        Assert.Equal(1, await CountRowsAsync());
    }

    private async Task<int> CountRowsAsync()
    {
        await using var db = new TenantConfigDbContext(new DbContextOptionsBuilder<TenantConfigDbContext>()
            .UseSqlServer(_connectionString)
            .Options);
        return await db.TestAuthAuthenticators.CountAsync(e => e.TenantId == TenantId && e.NormalizedEmail == Email);
    }

    private static string Setup(int n) => n.ToString("D32");

    /// <summary>Runs <see cref="BeforeNextSave"/> once, inside the next SaveChanges and before it writes.</summary>
    private sealed class BeforeSaveInterceptor : SaveChangesInterceptor
    {
        public Func<Task>? BeforeNextSave { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (BeforeNextSave is { } beforeSave)
            {
                BeforeNextSave = null;
                await beforeSave();
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
