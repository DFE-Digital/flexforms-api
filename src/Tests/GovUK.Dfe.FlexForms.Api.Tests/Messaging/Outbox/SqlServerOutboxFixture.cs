using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Tests.Common.Seeders;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Messaging.Outbox;

/// <summary>
/// One SQL Server container for the outbox acceptance tests and the Test Authentication authenticator store tests.
/// Each test gets its own database: EA with the real migrations (outbox tables, source revisions) and the standard
/// test data, or TenantConfig with the real migrations and one tenant.
/// </summary>
public sealed class SqlServerOutboxFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public async Task<string> CreateDatabaseAsync()
    {
        var connectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = $"ea_{Guid.NewGuid():N}",
        }.ConnectionString;

        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddDbContext<ExternalApplicationsContext>(o => o.UseSqlServer(connectionString))
            .BuildServiceProvider();
        await using (services)
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ExternalApplicationsContext>();
            await db.Database.MigrateAsync();
            // The migrations insert the system roles that the seeder also creates.
            await db.Database.ExecuteSqlRawAsync("DELETE FROM ea.TenantMemberships; DELETE FROM ea.Roles;");
            EaContextSeeder.SeedTestData(db);
        }

        return connectionString;
    }

    /// <summary>Creates a TenantConfig database with the real migrations and a tenant with <paramref name="tenantId"/>.</summary>
    public async Task<string> CreateTenantConfigDatabaseAsync(Guid tenantId)
    {
        var connectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = $"tenantconfig_{Guid.NewGuid():N}",
        }.ConnectionString;

        await using var db = new TenantConfigDbContext(new DbContextOptionsBuilder<TenantConfigDbContext>()
            .UseSqlServer(connectionString)
            .Options);
        await db.Database.MigrateAsync();
        db.Tenants.Add(new GovUK.Dfe.FlexForms.Domain.Tenancy.Entities.TenantEntity { Id = tenantId, Name = $"tenant-{tenantId:N}" });
        await db.SaveChangesAsync();

        return connectionString;
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerOutboxCollection : ICollectionFixture<SqlServerOutboxFixture>
{
    public const string Name = "Outbox SQL Server";
}
