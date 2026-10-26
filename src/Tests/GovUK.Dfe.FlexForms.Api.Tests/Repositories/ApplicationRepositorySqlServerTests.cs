using GovUK.Dfe.FlexForms.Api.Tests.Messaging.Outbox;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Infrastructure.Repositories;
using GovUK.Dfe.FlexForms.Tests.Common.Seeders;
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
}
