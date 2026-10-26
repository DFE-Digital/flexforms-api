using GovUK.Dfe.FlexForms.Application.Applications.Queries;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Tests.QueryObjects.Applications;

/// <summary>
/// Builds the listing SQL for SQL Server without a database, so a query EF cannot translate fails here.
/// </summary>
public sealed class ApplicationListingSqlTranslationTests(ITestOutputHelper output) : IDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection()
        .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
        .AddDbContext<ExternalApplicationsContext>(o => o.UseSqlServer("Server=unused;Database=unused"))
        .BuildServiceProvider();

    private ExternalApplicationsContext Db => _services.GetRequiredService<ExternalApplicationsContext>();

    [Fact]
    public void ProjectListingRows_TranslatesWithoutReadingTheSchema()
    {
        var sql = ApplicationListingQueryBuilder.ProjectListingRows(Db.Applications, includeSchema: false).ToQueryString();
        output.WriteLine(sql);

        Assert.Contains("[Name]", sql);
        Assert.DoesNotContain("JsonSchema", sql);
    }

    [Fact]
    public void ProjectListingRows_TranslatesWithTheSchema()
    {
        var sql = ApplicationListingQueryBuilder.ProjectListingRows(Db.Applications, includeSchema: true).ToQueryString();
        output.WriteLine(sql);

        Assert.Contains("[JsonSchema]", sql);
        Assert.Contains("[VersionNumber]", sql);
    }

    [Fact]
    public void LatestResponsesQuery_TranslatesToNewestResponseOnly()
    {
        var sql = new ApplicationRepository(Db)
            .LatestResponsesQuery([new ApplicationId(Guid.NewGuid())])
            .ToQueryString();
        output.WriteLine(sql);

        Assert.Contains("NOT EXISTS", sql);
        Assert.Contains("[CreatedOn] > ", sql);
    }

    public void Dispose() => _services.Dispose();
}
