using GovUK.Dfe.FlexForms.Api.Middleware;
using GovUK.Dfe.FlexForms.Utils.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GovUK.Dfe.FlexForms.Api.Tests.Middleware;

public class RequestTimingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_LogsTheBreakdown_ForTimedPaths()
    {
        var logger = new RecordingLogger();
        var middleware = new RequestTimingMiddleware(
            _ =>
            {
                RequestTimings.Record("redis pattern removal", TimeSpan.FromMilliseconds(400));
                return Task.CompletedTask;
            },
            Configuration(("RequestTimings:LogAboveMs", "0")),
            logger);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/v1/Applications";

        await middleware.InvokeAsync(context);

        var (level, message) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, level);
        Assert.StartsWith("[TIMING] POST /v1/Applications took ", message);
        Assert.Contains("redis pattern removal=400 ms", message);
        Assert.Null(RequestTimings.Current);
    }

    [Fact]
    public async Task InvokeAsync_LogsFastRequestsAtDebug()
    {
        var logger = new RecordingLogger();
        var middleware = new RequestTimingMiddleware(_ => Task.CompletedTask, Configuration(), logger);
        var context = new DefaultHttpContext();
        context.Request.Path = "/v1/me/permissions";

        await middleware.InvokeAsync(context);

        Assert.Equal(LogLevel.Debug, Assert.Single(logger.Entries).Level);
    }

    [Fact]
    public async Task InvokeAsync_SkipsPathsOutsideThePrefixes()
    {
        var logger = new RecordingLogger();
        RequestTimings? seen = null;
        var middleware = new RequestTimingMiddleware(
            _ =>
            {
                seen = RequestTimings.Current;
                return Task.CompletedTask;
            },
            Configuration(),
            logger);
        var context = new DefaultHttpContext();
        context.Request.Path = "/health";

        await middleware.InvokeAsync(context);

        Assert.Null(seen);
        Assert.Empty(logger.Entries);
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    private sealed class RecordingLogger : ILogger<RequestTimingMiddleware>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
