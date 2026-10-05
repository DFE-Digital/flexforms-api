using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GovUK.Dfe.FlexForms.Infrastructure.Prism;

/// <summary>
/// Calls Prism's export control endpoints with the API's identity, naming the signed-in admin in
/// <c>X-Prism-Acting-User</c>. Prism failures become <see cref="Result{T}"/> failures with a message for the admin.
/// </summary>
public sealed partial class PrismExportControlClient(
    HttpClient http,
    IOptions<PrismControlApiOptions> options,
    IPrismAccessTokenSource tokens,
    ILogger<PrismExportControlClient> logger) : IPrismExportControlClient
{
    public const string ActingUserHeader = "X-Prism-Acting-User";
    public const string DevelopmentKeyHeader = "X-Prism-Development-Key";

    internal const string NotConfigured = "Reporting export is not set up in this environment.";
    internal const string Unavailable = "The reporting service could not be reached. Try again later.";
    internal const string NotAllowed = "FlexForms is not allowed to manage reporting export. Ask the platform team to check its Prism roles.";
    internal const string NotCatalogued = "This template's fields are not known to reporting yet. They appear once the template is published or an application is saved.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<Result<ReportingExportPolicyDto>> GetPolicyAsync(Guid tenantId, Guid templateId, CancellationToken cancellationToken)
    {
        var response = await SendAsync<PrismPolicyView>(HttpMethod.Get, $"{TemplatePath(tenantId, templateId)}/export-policy", null, null, cancellationToken);
        return response.Map(p => new ReportingExportPolicyDto(
            p.TemplateId,
            p.PolicyVersion,
            p.DefaultMode,
            p.DefaultSource,
            p.Fields.Select(f => new ReportingExportFieldDto(
                f.ParentFieldId, f.FieldId, f.Label, f.DataType, f.IsCollection, f.TaskName, f.PageTitle,
                f.TemplateVersionNumber, f.ExportStatus, f.Reason, f.DecidedBy, f.DecidedAt)).ToList()),
            notFound: NotCatalogued);
    }

    public async Task<Result<ReportingExportChangeResultDto>> ChangeDecisionsAsync(
        Guid tenantId,
        Guid templateId,
        IReadOnlyList<ReportingExportDecisionRequest> decisions,
        string actingUser,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            Decisions = decisions.Select(d => new { d.ParentFieldId, d.FieldId, d.Decision, d.Reason }),
        };
        var response = await SendAsync<PrismChange<PrismApplyResult>>(
            HttpMethod.Put, $"{TemplatePath(tenantId, templateId)}/export-policy", body, actingUser, cancellationToken);
        return response.Map(c => new ReportingExportChangeResultDto(
            Status(c.Result.Status), c.Result.PolicyVersion, c.Result.Changed, c.Result.Warnings, c.Backfill?.OperationId));
    }

    public async Task<Result<ReportingExportDefaultDto>> GetDefaultAsync(Guid tenantId, Guid? templateId, CancellationToken cancellationToken)
    {
        var response = await SendAsync<PrismDefaultView>(HttpMethod.Get, DefaultPath(tenantId, templateId), null, null, cancellationToken);
        return response.Map(ToDto);
    }

    public async Task<Result<ReportingExportChangeResultDto>> ChangeDefaultAsync(
        Guid tenantId,
        Guid? templateId,
        UpdateReportingExportDefaultRequest request,
        string actingUser,
        CancellationToken cancellationToken)
    {
        var body = new { request.Mode, request.Reason };
        var response = await SendAsync<PrismChange<PrismDefaultChangeResult>>(
            HttpMethod.Put, DefaultPath(tenantId, templateId), body, actingUser, cancellationToken);
        return response.Map(c => new ReportingExportChangeResultDto(
            Status(c.Result.Status), c.Result.PolicyVersion, c.Result.Status == "Applied" ? 1 : 0, [], c.Backfill?.OperationId));
    }

    public async Task<Result<ReportingExportRefreshDto>> GetRefreshAsync(Guid tenantId, Guid refreshId, CancellationToken cancellationToken)
    {
        var response = await SendAsync<PrismOperation>(HttpMethod.Get, $"api/control/backfill/{refreshId}", null, null, cancellationToken);
        if (response.Value is { } operation && operation.TenantId != tenantId)
        {
            return Result<ReportingExportRefreshDto>.NotFound("Refresh not found.");
        }

        return response.Map(o => new ReportingExportRefreshDto(
            o.OperationId, o.Status, o.ApplicationsScanned, o.MessagesEnqueued, o.Error, o.CreatedAt, o.StartedAt, o.CompletedAt),
            notFound: "Refresh not found.");
    }

    private static string TemplatePath(Guid tenantId, Guid templateId) => $"api/control/tenants/{tenantId}/templates/{templateId}";

    private static string DefaultPath(Guid tenantId, Guid? templateId) => templateId is { } id
        ? $"{TemplatePath(tenantId, id)}/export-default"
        : $"api/control/tenants/{tenantId}/export-default";

    private static ReportingExportChangeStatus Status(string status) =>
        status == "Unchanged" ? ReportingExportChangeStatus.Unchanged : ReportingExportChangeStatus.Applied;

    private static ReportingExportDefaultDto ToDto(PrismDefaultView d) =>
        new(d.TemplateId, d.Mode, d.EffectiveMode, d.Source, d.Reason, d.DecidedBy, d.DecidedAt);

    private async Task<PrismResponse<T>> SendAsync<T>(
        HttpMethod method, string path, object? body, string? actingUser, CancellationToken cancellationToken)
        where T : class
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return PrismResponse<T>.Failed(NotConfigured);
        }

        try
        {
            using var request = new HttpRequestMessage(method, new Uri(new Uri(EnsureTrailingSlash(settings.BaseUrl)), path));
            if (!string.IsNullOrEmpty(settings.DevelopmentKey))
            {
                request.Headers.Add(DevelopmentKeyHeader, settings.DevelopmentKey);
            }
            else if (!string.IsNullOrWhiteSpace(settings.Scope))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(settings.Scope, cancellationToken));
            }
            else
            {
                return PrismResponse<T>.Failed(NotConfigured);
            }

            if (actingUser is not null)
            {
                request.Headers.Add(ActingUserHeader, actingUser);
            }

            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: Json);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(settings.Timeout);
            using var response = await http.SendAsync(request, timeout.Token);

            if (response.IsSuccessStatusCode)
            {
                return PrismResponse<T>.Ok((await response.Content.ReadFromJsonAsync<T>(Json, timeout.Token))!);
            }

            return await FailureAsync<T>(response, path, timeout.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or Azure.Identity.AuthenticationFailedException
                                   && !cancellationToken.IsCancellationRequested)
        {
            LogUnavailable(ex, path);
            return PrismResponse<T>.Failed(Unavailable);
        }
    }

    private async Task<PrismResponse<T>> FailureAsync<T>(HttpResponseMessage response, string path, CancellationToken cancellationToken)
        where T : class
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                return PrismResponse<T>.NotFound();
            case HttpStatusCode.BadRequest:
                return PrismResponse<T>.Validation(await ProblemsAsync(response, " ", cancellationToken));
            case HttpStatusCode.UnprocessableEntity:
                return PrismResponse<T>.Validation(
                    $"These fields are not in the template: {await ProblemsAsync(response, ", ", cancellationToken)}.");
            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                LogRejected((int)response.StatusCode, path);
                return PrismResponse<T>.Failed(NotAllowed);
            default:
                LogFailed((int)response.StatusCode, path);
                return PrismResponse<T>.Failed(Unavailable);
        }
    }

    /// <summary>Prism returns either a change result with problems, or a problem document.</summary>
    private static async Task<string> ProblemsAsync(HttpResponseMessage response, string separator, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            if (root.TryGetProperty("result", out var result) && result.TryGetProperty("problems", out var problems)
                && problems.ValueKind == JsonValueKind.Array && problems.GetArrayLength() > 0)
            {
                return string.Join(separator, problems.EnumerateArray().Select(p => p.GetString()));
            }

            if (root.TryGetProperty("detail", out var detail) && detail.GetString() is { Length: > 0 } text)
            {
                return text;
            }
        }
        catch (JsonException)
        {
        }

        return "Reporting rejected the change.";
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";

    [LoggerMessage(Level = LogLevel.Error, Message = "Prism rejected the API's credentials ({Status}) for {Path}; check the API identity has Prism.Admin and Prism.Delegate")]
    private partial void LogRejected(int status, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Prism returned {Status} for {Path}")]
    private partial void LogFailed(int status, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Prism could not be reached for {Path}")]
    private partial void LogUnavailable(Exception exception, string path);

    private sealed record PrismResponse<T>(T? Value, DomainErrorCode? ErrorCode, string? Error)
        where T : class
    {
        public static PrismResponse<T> Ok(T value) => new(value, null, null);

        public static PrismResponse<T> NotFound() => new(null, DomainErrorCode.NotFound, null);

        public static PrismResponse<T> Validation(string error) => new(null, DomainErrorCode.Validation, error);

        public static PrismResponse<T> Failed(string error) => new(null, DomainErrorCode.BadRequest, error);

        public Result<TOut> Map<TOut>(Func<T, TOut> map, string notFound = "Not found.") => ErrorCode switch
        {
            null => Result<TOut>.Success(map(Value!)),
            DomainErrorCode.NotFound => Result<TOut>.NotFound(notFound),
            DomainErrorCode.Validation => Result<TOut>.Validation(Error!),
            _ => Result<TOut>.Failure(Error!),
        };
    }

    private sealed record PrismField(
        string ParentFieldId,
        string FieldId,
        string? Label,
        string? DataType,
        bool IsCollection,
        string? TaskName,
        string? PageTitle,
        string? TemplateVersionNumber,
        ReportingExportStatus ExportStatus,
        string? Reason,
        string? DecidedBy,
        DateTime? DecidedAt);

    private sealed record PrismPolicyView(
        Guid TenantId,
        Guid TemplateId,
        int PolicyVersion,
        ReportingExportMode DefaultMode,
        ReportingExportDefaultSource DefaultSource,
        List<PrismField> Fields);

    private sealed record PrismDefaultView(
        Guid TenantId,
        Guid? TemplateId,
        ReportingExportModeSetting Mode,
        ReportingExportMode EffectiveMode,
        ReportingExportDefaultSource Source,
        string? Reason,
        string? DecidedBy,
        DateTime? DecidedAt);

    private sealed record PrismApplyResult(string Status, int PolicyVersion, int Changed, List<string> Warnings);

    private sealed record PrismDefaultChangeResult(string Status, int PolicyVersion);

    private sealed record PrismChange<TResult>(TResult Result, PrismOperation? Backfill);

    private sealed record PrismOperation(
        Guid OperationId,
        Guid? TenantId,
        ReportingExportRefreshStatus Status,
        int ApplicationsScanned,
        int MessagesEnqueued,
        string? Error,
        DateTime CreatedAt,
        DateTime? StartedAt,
        DateTime? CompletedAt);
}
