using GovUK.Dfe.FlexForms.Application.Common.Behaviours;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Utils.Diagnostics;
using MediatR;

namespace GovUK.Dfe.FlexForms.Application.Tests.Diagnostics;

public sealed class RequestTimingsTests : IDisposable
{
    [Fact]
    public void Steps_AreIgnored_OutsideATimedRequest()
    {
        RequestTimings.Stop();

        using (RequestTimings.Measure("handler"))
        {
        }
        RequestTimings.Record("sql SELECT Users", TimeSpan.FromMilliseconds(5));
        RequestTimings.Count("keys");

        Assert.Null(RequestTimings.Current);
    }

    [Fact]
    public void Describe_ListsStepsSlowestFirst_WithCounts()
    {
        var timings = RequestTimings.Start();

        RequestTimings.Record("sql SELECT Users", TimeSpan.FromMilliseconds(10));
        RequestTimings.Record("sql SELECT Users", TimeSpan.FromMilliseconds(15));
        RequestTimings.Record("redis pattern removal", TimeSpan.FromMilliseconds(400));
        RequestTimings.Count("keys removed", 3);

        Assert.Equal("redis pattern removal=400 ms, sql SELECT Users=25 ms x2, keys removed=3", timings.Describe());
    }

    [Theory]
    [InlineData("SELECT TOP(1) [u].[UserId] FROM [ea].[Users] AS [u] WHERE [u].[Email] = @email", "sql SELECT Users")]
    [InlineData("SET NOCOUNT ON;\r\nINSERT INTO [ea].[Permissions] ([PermissionId]) VALUES (@p0);", "sql INSERT Permissions")]
    [InlineData("UPDATE [ea].[Applications] SET [LastModifiedOn] = @p0 WHERE [ApplicationId] = @p1;", "sql UPDATE Applications")]
    [InlineData("DELETE FROM [ea].[Permissions] WHERE [PermissionId] = @p0;", "sql DELETE Permissions")]
    [InlineData("MERGE [ea].[Permissions] USING (VALUES (@p0)) AS i ([PermissionId]) ON 1=0", "sql MERGE Permissions")]
    [InlineData("UPDATE [a] SET [a].[LastModifiedOn] = @p0 FROM [ea].[Applications] AS [a] WHERE [a].[ApplicationId] = @p1", "sql UPDATE Applications")]
    [InlineData("SELECT [t].[Id] FROM [Templates] AS [t]", "sql SELECT Templates")]
    [InlineData("SELECT 1", "sql other")]
    public void StepName_NamesTheCommandByVerbAndFirstTable(string commandText, string expected)
    {
        Assert.Equal(expected, RequestTimingCommandInterceptor.StepName(commandText));
    }

    [Fact]
    public async Task RequestTimingBehaviour_RecordsTheHandlerByRequestName()
    {
        var timings = RequestTimings.Start();
        var behaviour = new RequestTimingBehaviour<SampleQuery, int>();

        var result = await behaviour.Handle(new SampleQuery(), _ => Task.FromResult(42), CancellationToken.None);

        Assert.Equal(42, result);
        Assert.True(timings.Steps.ContainsKey("handler SampleQuery"));
    }

    public void Dispose() => RequestTimings.Stop();

    public sealed record SampleQuery : IRequest<int>;
}
