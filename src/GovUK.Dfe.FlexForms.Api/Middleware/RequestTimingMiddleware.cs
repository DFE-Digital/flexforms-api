using GovUK.Dfe.FlexForms.Utils.Diagnostics;

namespace GovUK.Dfe.FlexForms.Api.Middleware;

/// <summary>
/// Logs one "[TIMING]" line per request under the configured paths, with the total time and each step measured
/// with <see cref="RequestTimings"/>: handlers, SQL commands by table, permission claims and Redis removals.
/// The instance name shows which replica served the request, since each one compiles its EF queries on first use.
/// </summary>
/// <remarks>
/// Settings (all optional): <c>RequestTimings:Enabled</c> (default true), <c>RequestTimings:PathPrefixes</c>
/// (default "/v1" and "/auth"), <c>RequestTimings:LogAboveMs</c> (default 100; faster requests are logged at Debug)
/// and <c>RequestTimings:SlowRequestMs</c> (default 1000; slower requests are logged as warnings).
/// </remarks>
public sealed class RequestTimingMiddleware(
    RequestDelegate next,
    IConfiguration configuration,
    ILogger<RequestTimingMiddleware> logger)
{
    private readonly bool _enabled = configuration.GetValue("RequestTimings:Enabled", true);
    private readonly string[] _pathPrefixes = configuration.GetSection("RequestTimings:PathPrefixes").Get<string[]>() is { Length: > 0 } prefixes
        ? prefixes
        : ["/v1", "/auth"];
    private readonly double _logAboveMs = configuration.GetValue("RequestTimings:LogAboveMs", 100d);
    private readonly double _slowRequestMs = configuration.GetValue("RequestTimings:SlowRequestMs", 1000d);

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_enabled || !ShouldTime(context.Request.Path))
        {
            await next(context);
            return;
        }

        var timings = RequestTimings.Start();
        try
        {
            await next(context);
        }
        finally
        {
            RequestTimings.Stop();
            var totalMs = Math.Round(timings.Elapsed.TotalMilliseconds);
            var level = totalMs >= _slowRequestMs ? LogLevel.Warning
                : totalMs >= _logAboveMs ? LogLevel.Information
                : LogLevel.Debug;
            logger.Log(
                level,
                "[TIMING] {Method} {Path} took {TotalMs} ms with status {StatusCode} on {Instance}: {Breakdown}",
                context.Request.Method,
                context.Request.Path.Value,
                totalMs,
                context.Response.StatusCode,
                Environment.MachineName,
                timings.Describe());
        }
    }

    private bool ShouldTime(PathString path) =>
        _pathPrefixes.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
}
