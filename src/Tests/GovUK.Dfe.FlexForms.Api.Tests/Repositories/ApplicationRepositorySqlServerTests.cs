using GovUK.Dfe.FlexForms.Api.Tests.Messaging.Outbox;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Infrastructure.Repositories;
using GovUK.Dfe.FlexForms.Tests.Common.Seeders;
using GovUK.Dfe.FlexForms.Utils.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Api.Tests.Repositories;

[Collection(SqlServerOutboxCollection.Name)]
public sealed class ApplicationRepositorySqlServerTests(SqlServerOutboxFixture sql)
{
    [Fact]
    public async Task GetLatestResponsesAsync_ReturnsOnlyTheNewestResponsePerApplication()
    {
        var connectionString = await sql.CreateDatabaseAsync();
        await using var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddDbContext<ExternalApplicationsContext>(o => o.UseSqlServer(connectionString))
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExternalApplicationsContext>();
        var seededApplication = new ApplicationId(new Guid(EaContextSeeder.ApplicationId));

        var latest = await new ApplicationRepository(db).GetLatestResponsesAsync(
            [seededApplication, new ApplicationId(Guid.NewGuid())],
            CancellationToken.None);

        var (applicationId, response) = Assert.Single(latest);
        Assert.Equal(seededApplication, applicationId);
        Assert.Equal(new Guid(EaContextSeeder.ResponseId2), response.Id!.Value);
        Assert.Equal("Reviewed and approved", response.ResponseBody);
    }

    [Fact]
    public async Task RequestTimingInterceptor_RecordsEachCommand_AndCountsCompilationOncePerQueryShape()
    {
        var connectionString = await sql.CreateDatabaseAsync();
        await using var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddDbContext<ExternalApplicationsContext>(o => o
                .UseSqlServer(connectionString)
                .AddInterceptors(RequestTimingCommandInterceptor.Instance))
            .BuildServiceProvider();
        var probeId = Guid.NewGuid().ToString("N");
        var timings = RequestTimings.Start();
        try
        {
            for (var i = 0; i < 2; i++)
            {
                await using var scope = services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ExternalApplicationsContext>();
                await db.Users.AsNoTracking().Where(u => u.Email == probeId).ToListAsync();
            }
        }
        finally
        {
            RequestTimings.Stop();
        }

        Assert.Equal(2, timings.Steps["sql SELECT Users"].Count);
        Assert.Equal(1, timings.Steps["ef query compilations"].Count);
    }
}
