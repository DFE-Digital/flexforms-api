using System.Data.Common;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using GovUK.Dfe.FlexForms.Utils.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GovUK.Dfe.FlexForms.Infrastructure.Database;

/// <summary>
/// Adds each SQL command to the request timings as "sql {verb} {table}", for example "sql SELECT Permissions=40 ms x3".
/// Reader time runs from execution until the reader is disposed, so it includes reading the rows, not just the first response.
/// Also counts "ef query compilations": EF compiles a query shape once per instance, and that time comes before any SQL.
/// </summary>
public sealed partial class RequestTimingCommandInterceptor : DbCommandInterceptor, IQueryExpressionInterceptor
{
    public static readonly RequestTimingCommandInterceptor Instance = new();

    private static readonly ConditionalWeakTable<DbCommand, StrongBox<long>> ReaderStarts = new();

    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        RequestTimings.Count("ef query compilations");
        return queryExpression;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        MarkReaderStart(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        MarkReaderStart(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult DataReaderDisposing(
        DbCommand command,
        DataReaderDisposingEventData eventData,
        InterceptionResult result)
    {
        // SQL Server can answer before it has found any rows, so time the reader from execution to disposal.
        var elapsed = ReaderStarts.TryGetValue(command, out var startedAt)
            ? Stopwatch.GetElapsedTime(startedAt.Value)
            : eventData.Duration;
        ReaderStarts.Remove(command);

        RequestTimings.Record(StepName(command.CommandText), elapsed);
        return result;
    }

    private static void MarkReaderStart(DbCommand command)
    {
        if (RequestTimings.Current is not null)
            ReaderStarts.AddOrUpdate(command, new StrongBox<long>(Stopwatch.GetTimestamp()));
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        RequestTimings.Record(StepName(command.CommandText), eventData.Duration);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        RequestTimings.Record(StepName(command.CommandText), eventData.Duration);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        RequestTimings.Record(StepName(command.CommandText), eventData.Duration);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        RequestTimings.Record(StepName(command.CommandText), eventData.Duration);
        return ValueTask.FromResult(result);
    }

    /// <summary>Names a command by its first statement and table; values are parameters, so none appear here.</summary>
    public static string StepName(string commandText)
    {
        var verb = FirstStatementVerb().Match(commandText);
        // Bulk updates start "UPDATE [a] SET ... FROM [ea].[Applications] AS [a]", so prefer a schema-qualified table.
        var table = QualifiedTable().Match(commandText) is { Success: true } qualified
            ? qualified
            : UnqualifiedTable().Match(commandText);
        if (!verb.Success || !table.Success)
            return "sql other";

        return $"sql {verb.Value.ToUpperInvariant()} {table.Groups["table"].Value}";
    }

    [GeneratedRegex(@"\b(SELECT|INSERT|UPDATE|DELETE|MERGE)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FirstStatementVerb();

    [GeneratedRegex(@"\b(?:FROM|INTO|UPDATE|MERGE|JOIN)\s+\[\w+\]\.\[(?<table>\w+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex QualifiedTable();

    [GeneratedRegex(@"\b(?:FROM|INTO|UPDATE|MERGE|JOIN)\s+\[(?<table>\w+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex UnqualifiedTable();
}
