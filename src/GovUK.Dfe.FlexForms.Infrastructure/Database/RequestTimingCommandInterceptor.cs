using System.Data.Common;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using GovUK.Dfe.FlexForms.Utils.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GovUK.Dfe.FlexForms.Infrastructure.Database;

/// <summary>
/// Adds each SQL command to the request timings as "sql {verb} {table}", for example "sql SELECT Permissions=40 ms x3".
/// Reader time runs until the reader is disposed, so it includes reading the rows, not just running the query.
/// Also counts "ef query compilations": EF compiles a query shape once per instance, and that time comes before any SQL.
/// </summary>
public sealed partial class RequestTimingCommandInterceptor : DbCommandInterceptor, IQueryExpressionInterceptor
{
    public static readonly RequestTimingCommandInterceptor Instance = new();

    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        RequestTimings.Count("ef query compilations");
        return queryExpression;
    }

    public override InterceptionResult DataReaderDisposing(
        DbCommand command,
        DataReaderDisposingEventData eventData,
        InterceptionResult result)
    {
        RequestTimings.Record(StepName(command.CommandText), eventData.Duration);
        return result;
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
        var match = FirstTableStatement().Match(commandText);
        if (!match.Success)
            return "sql other";

        var keyword = match.Groups["verb"].Value.ToUpperInvariant();
        var verb = keyword.StartsWith("DELETE", StringComparison.Ordinal) ? "DELETE"
            : keyword == "FROM" ? "SELECT"
            : keyword == "INTO" ? "INSERT"
            : keyword;
        return $"sql {verb} {match.Groups["table"].Value}";
    }

    [GeneratedRegex(@"\b(?<verb>DELETE\s+FROM|FROM|INTO|UPDATE|MERGE)\s+(?:\[\w+\]\.)?\[(?<table>\w+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex FirstTableStatement();
}
