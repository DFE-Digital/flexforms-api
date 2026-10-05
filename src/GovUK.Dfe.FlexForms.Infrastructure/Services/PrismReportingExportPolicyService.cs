using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Infrastructure.Configurations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GovUK.Dfe.FlexForms.Infrastructure.Services;

/// <summary>
/// Reads and changes the reporting export policy through Prism's control API, with the API's own identity and the
/// signed-in admin named in <c>X-Prism-Acting-User</c>.
/// </summary>
public sealed partial class PrismReportingExportPolicyService(
    HttpClient http,
    IOptions<PrismControlApiOptions> options,
    IPrismAccessTokenSource tokens,
    ILogger<PrismReportingExportPolicyService> logger) : IReportingExportPolicyService
{
    public const string ActingUserHeader = "X-Prism-Acting-User";
    public const string DevelopmentKeyHeader = "X-Prism-Development-Key";

    internal const string NotConfigured = "Reporting export is not set up in this environment.";
    internal const string Unavailable = "The reporting service could not be reached. Try again later.";
    internal const string NotAllowed = "FlexForms is not allowed to manage reporting export. Ask the platform team to check its Prism roles.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<ReportingExportPolicyDto?> GetPolicyAsync(Guid tenantId, Guid templateId, CancellationToken cancellationToken = default)
    {
        var policy = await SendAsync<PrismPolicyView>(HttpMethod.Get, $"{TemplatePath(tenantId, templateId)}/export-policy", null, null, cancellationToken);
        return policy is null
            ? null
            : new ReportingExportPolicyDto(
                policy.TemplateId,
                policy.PolicyVersion,
                policy.DefaultMode,
                policy.DefaultSource,
                policy.Fields.Select(f => new ReportingExportFieldDto(
                    f.ParentFieldId, f.FieldId, f.Label, f.DataType, f.IsCollection, f.TaskName, f.PageTitle,
                    f.TemplateVersionNumber, f.ExportStatus, f.Reason, f.DecidedBy, f.DecidedAt)).ToList());
    }

    public async Task<ReportingExportChangeResultDto?> ChangeDecisionsAsync(
        Guid tenantId,
        Guid templateId,
        IReadOnlyList<ReportingExportDecisionRequest> decisions,
        string actorEmail,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            Decisions = decisions.Select(d => new { d.ParentFieldId, d.FieldId, d.Decision, d.Reason }),
        };
        var change = await SendAsync<PrismChange<PrismApplyResult>>(
            HttpMethod.Put, $"{TemplatePath(tenantId, templateId)}/export-policy", body, actorEmail, cancellationToken);
        return change is null
            ? null
            : new ReportingExportChangeResultDto(
                Status(change.Result.Status), change.Result.PolicyVersion, change.Result.Changed, change.Result.Warnings, change.Backfill?.OperationId);
    }

    public async Task<ReportingExportDefaultDto?> GetDefaultAsync(Guid tenantId, Guid? templateId, CancellationToken cancellationToken = default)
    {
        var view = await SendAsync<PrismDefaultView>(HttpMethod.Get, DefaultPath(tenantId, templateId), null, null, cancellationToken);
        return view is null
            ? null
            : new ReportingExportDefaultDto(view.TemplateId, view.Mode, view.EffectiveMode, view.Source, view.Reason, view.DecidedBy, view.DecidedAt);
    }

    public async Task<ReportingExportChangeResultDto?> ChangeDefaultAsync(
        Guid tenantId,
        Guid? templateId,
        UpdateReportingExportDefaultRequest request,
        string actorEmail,
        CancellationToken cancellationToken = default)
    {
        var body = new { request.Mode, request.Reason };
        var change = await SendAsync<PrismChange<PrismDefaultChangeResult>>(
            HttpMethod.Put, DefaultPath(tenantId, templateId), body, actorEmail, cancellationToken);
        if (change is null)
        {
            return null;
        }

        var status = Status(change.Result.Status);
        return new ReportingExportChangeResultDto(
            status, change.Result.PolicyVersion, status == ReportingExportChangeStatus.Applied ? 1 : 0, [], change.Backfill?.OperationId);
    }

    public async Task<ReportingExportRefreshDto?> GetRefreshAsync(Guid tenantId, Guid refreshId, CancellationToken cancellationToken = default)
    {
        var operation = await SendAsync<PrismOperation>(HttpMethod.Get, $"api/control/backfill/{refreshId}", null, null, cancellationToken);
        return operation is null || operation.TenantId != tenantId
            ? null
            : new ReportingExportRefreshDto(
                operation.OperationId, operation.Status, operation.ApplicationsScanned, operation.MessagesEnqueued,
                operation.Error, operation.CreatedAt, operation.StartedAt, operation.CompletedAt);
    }

    private static string TemplatePath(Guid tenantId, Guid templateId) => $"api/control/tenants/{tenantId}/templates/{templateId}";

    private static string DefaultPath(Guid tenantId, Guid? templateId) => templateId is { } id
        ? $"{TemplatePath(tenantId, id)}/export-default"
        : $"api/control/tenants/{tenantId}/export-default";

    private static ReportingExportChangeStatus Status(string status) =>
        status == "Unchanged" ? ReportingExportChangeStatus.Unchanged : ReportingExportChangeStatus.Applied;

    /// <returns>The response body, or null when Prism answers 404.</returns>
    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, string? actorEmail, CancellationToken cancellationToken)
        where T : class
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.BaseUrl)
            || (string.IsNullOrEmpty(settings.DevelopmentKey) && string.IsNullOrWhiteSpace(settings.Scope)))
        {
            throw new InvalidOperationException(NotConfigured);
        }

        try
        {
            using var request = new HttpRequestMessage(method, new Uri(new Uri(EnsureTrailingSlash(settings.BaseUrl)), path));
            if (!string.IsNullOrEmpty(settings.DevelopmentKey))
            {
                request.Headers.Add(DevelopmentKeyHeader, settings.DevelopmentKey);
            }
            else
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(settings.Scope!, cancellationToken));
            }

            if (actorEmail is not null)
            {
                request.Headers.Add(ActingUserHeader, actorEmail);
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
                return await response.Content.ReadFromJsonAsync<T>(Json, timeout.Token);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            throw await FailureAsync(response, path, timeout.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or Azure.Identity.AuthenticationFailedException
                                   && !cancellationToken.IsCancellationRequested)
        {
            LogUnavailable(ex, path);
            throw new InvalidOperationException(Unavailable, ex);
        }
    }

    private async Task<Exception> FailureAsync(HttpResponseMessage response, string path, CancellationToken cancellationToken)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.BadRequest:
                return new ArgumentException(await ProblemsAsync(response, " ", cancellationToken));
            case HttpStatusCode.UnprocessableEntity:
                return new ArgumentException($"These fields are not in the template: {await ProblemsAsync(response, ", ", cancellationToken)}.");
            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                LogRejected((int)response.StatusCode, path);
                return new InvalidOperationException(NotAllowed);
            default:
                LogFailed((int)response.StatusCode, path);
                return new InvalidOperationException(Unavailable);
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
