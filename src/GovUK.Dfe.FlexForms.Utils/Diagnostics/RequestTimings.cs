using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace GovUK.Dfe.FlexForms.Utils.Diagnostics;

/// <summary>
/// Where the time goes in one API request. The request timing middleware starts it, and code anywhere in the request
/// adds steps with <see cref="Measure"/>, <see cref="Record"/> or <see cref="Count"/>. Outside a timed request they do
/// nothing, so they are safe to call from tests, message consumers and background work.
/// </summary>
/// <remarks>Steps can contain other steps, so their times overlap and don't add up to the total.</remarks>
public sealed class RequestTimings
{
    private static readonly AsyncLocal<RequestTimings?> CurrentTimings = new();

    private readonly ConcurrentDictionary<string, Step> _steps = new(StringComparer.Ordinal);
    private readonly long _startedAt = Stopwatch.GetTimestamp();

    public static RequestTimings? Current => CurrentTimings.Value;

    public static RequestTimings Start() => CurrentTimings.Value = new RequestTimings();

    public static void Stop() => CurrentTimings.Value = null;

    /// <summary>Times the code until the result is disposed: <c>using (RequestTimings.Measure("permission claims")) { ... }</c>.</summary>
    public static Measurement Measure(string step) => new(Current, step);

    /// <summary>Adds a duration measured elsewhere, such as the time EF reports for a SQL command.</summary>
    public static void Record(string step, TimeSpan elapsed) => Current?.Add(step, elapsed, 1, timed: true);

    /// <summary>Adds to a count, such as keys removed, without timing anything.</summary>
    public static void Count(string step, long amount = 1) => Current?.Add(step, TimeSpan.Zero, amount, timed: false);

    public TimeSpan Elapsed => Stopwatch.GetElapsedTime(_startedAt);

    public IReadOnlyDictionary<string, Step> Steps => _steps;

    /// <summary>The steps, slowest first, for example <c>handler CreateApplicationCommand=1102 ms, redis pattern removal=840 ms x6</c>.</summary>
    public string Describe() =>
        string.Join(", ", _steps
            .OrderByDescending(step => step.Value.Elapsed)
            .ThenBy(step => step.Key, StringComparer.Ordinal)
            .Select(step => $"{step.Key}={step.Value}"));

    private void Add(string step, TimeSpan elapsed, long amount, bool timed) =>
        _steps.AddOrUpdate(
            step,
            _ => new Step(elapsed, amount, timed),
            (_, existing) => new Step(existing.Elapsed + elapsed, existing.Count + amount, existing.Timed || timed));

    public readonly record struct Step(TimeSpan Elapsed, long Count, bool Timed)
    {
        public override string ToString()
        {
            if (!Timed)
                return Count.ToString(CultureInfo.InvariantCulture);

            var milliseconds = Math.Round(Elapsed.TotalMilliseconds).ToString(CultureInfo.InvariantCulture) + " ms";
            return Count > 1 ? $"{milliseconds} x{Count.ToString(CultureInfo.InvariantCulture)}" : milliseconds;
        }
    }

    public readonly struct Measurement : IDisposable
    {
        private readonly RequestTimings? _timings;
        private readonly string _step;
        private readonly long _startedAt;

        internal Measurement(RequestTimings? timings, string step)
        {
            _timings = timings;
            _step = step;
            _startedAt = timings is null ? 0 : Stopwatch.GetTimestamp();
        }

        public void Dispose() => _timings?.Add(_step, Stopwatch.GetElapsedTime(_startedAt), 1, timed: true);
    }
}
