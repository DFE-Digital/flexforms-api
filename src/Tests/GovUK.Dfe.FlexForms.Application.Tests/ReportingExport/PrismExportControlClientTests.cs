using System.Net;
using System.Text;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.FlexForms.Infrastructure.Prism;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.ReportingExport;

public class PrismExportControlClientTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid TemplateId = Guid.Parse("33333333-3333-4333-8333-333333333333");

    private readonly StubHandler _handler = new();
    private readonly IPrismAccessTokenSource _tokens = Substitute.For<IPrismAccessTokenSource>();
    private readonly PrismControlApiOptions _options = new() { BaseUrl = "http://prism.local/", DevelopmentKey = "dev-key" };

    private PrismExportControlClient Client() =>
        new(new HttpClient(_handler), Microsoft.Extensions.Options.Options.Create(_options), _tokens, NullLogger<PrismExportControlClient>.Instance);

    [Fact]
    public async Task GetPolicy_ShouldMapPrismView()
    {
        _handler.Respond(HttpStatusCode.OK, $$"""
            {"tenantId":"{{TenantId}}","templateId":"{{TemplateId}}","policyVersion":7,"defaultMode":"ExportAll","defaultSource":"Template",
             "fields":[{"parentFieldId":"","fieldId":"email","label":"Email","dataType":"Text","isCollection":false,"taskName":"Contact",
                        "pageTitle":"Your details","templateVersionNumber":"1.2","exportStatus":"AllowedByDefault","reason":null,
                        "decidedBy":null,"decidedAt":null,"policyVersion":null}]}
            """);

        var result = await Client().GetPolicyAsync(TenantId, TemplateId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Value!.PolicyVersion);
        Assert.Equal(ReportingExportMode.ExportAll, result.Value.DefaultMode);
        Assert.Equal(ReportingExportDefaultSource.Template, result.Value.DefaultSource);
        var field = Assert.Single(result.Value.Fields);
        Assert.Equal(ReportingExportStatus.AllowedByDefault, field.Status);
        Assert.Equal("Your details", field.PageTitle);
        Assert.Equal($"http://prism.local/api/control/tenants/{TenantId}/templates/{TemplateId}/export-policy", _handler.Request!.RequestUri!.ToString());
        Assert.Equal("dev-key", _handler.Request.Headers.GetValues(PrismExportControlClient.DevelopmentKeyHeader).Single());
    }

    [Fact]
    public async Task GetPolicy_ShouldExplainUncataloguedTemplate_OnNotFound()
    {
        _handler.Respond(HttpStatusCode.NotFound, "");

        var result = await Client().GetPolicyAsync(TenantId, TemplateId, CancellationToken.None);

        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
        Assert.Equal(PrismExportControlClient.NotCatalogued, result.Error);
    }

    [Fact]
    public async Task ChangeDecisions_ShouldSendActingUserAndMapBackfill()
    {
        var operationId = Guid.NewGuid();
        _handler.Respond(HttpStatusCode.OK, $$"""
            {"result":{"status":"Applied","policyVersion":8,"changed":1,"problems":[],"warnings":["Email is personal data"]},
             "backfill":{"operationId":"{{operationId}}","tenantId":"{{TenantId}}","status":"Pending","applicationsScanned":0,
                         "messagesEnqueued":0,"error":null,"createdAt":"2026-10-05T10:00:00Z","startedAt":null,"completedAt":null} }
            """);

        var result = await Client().ChangeDecisionsAsync(
            TenantId,
            TemplateId,
            [new ReportingExportDecisionRequest { FieldId = "email", Decision = ReportingExportDecision.Allowed, Reason = "Needed" }],
            "ada@example.gov.uk",
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ReportingExportChangeStatus.Applied, result.Value!.Status);
        Assert.Equal(operationId, result.Value.RefreshId);
        Assert.Equal(["Email is personal data"], result.Value.Warnings);
        Assert.Equal(HttpMethod.Put, _handler.Request!.Method);
        Assert.Equal("ada@example.gov.uk", _handler.Request.Headers.GetValues(PrismExportControlClient.ActingUserHeader).Single());
        Assert.Contains("\"decision\":\"Allowed\"", _handler.Body);
        Assert.Contains("\"fieldId\":\"email\"", _handler.Body);
    }

    [Fact]
    public async Task ChangeDecisions_ShouldListUnknownFields_OnUnprocessableEntity()
    {
        _handler.Respond(HttpStatusCode.UnprocessableEntity, """
            {"result":{"status":"UnknownFields","policyVersion":3,"changed":0,"problems":["phone","fax"],"warnings":[]},"backfill":null}
            """);

        var result = await Client().ChangeDecisionsAsync(TenantId, TemplateId, [], "ada", CancellationToken.None);

        Assert.Equal(DomainErrorCode.Validation, result.ErrorCode);
        Assert.Equal("These fields are not in the template: phone, fax.", result.Error);
    }

    [Fact]
    public async Task ChangeDefault_ShouldUseTenantPath_WithoutTemplate()
    {
        _handler.Respond(HttpStatusCode.OK, """{"result":{"status":"Unchanged","policyVersion":2,"default":null,"problems":[]},"backfill":null}""");

        var result = await Client().ChangeDefaultAsync(
            TenantId, null, new UpdateReportingExportDefaultRequest { Mode = ReportingExportModeSetting.ApproveFirst }, "ada", CancellationToken.None);

        Assert.Equal(ReportingExportChangeStatus.Unchanged, result.Value!.Status);
        Assert.Null(result.Value.RefreshId);
        Assert.Equal($"http://prism.local/api/control/tenants/{TenantId}/export-default", _handler.Request!.RequestUri!.ToString());
        Assert.Contains("\"mode\":\"ApproveFirst\"", _handler.Body);
    }

    [Fact]
    public async Task ChangeDefault_ShouldSurfaceProblemDetail_OnBadRequest()
    {
        _handler.Respond(HttpStatusCode.BadRequest, """{"status":400,"detail":"A reason is required to export everything."}""");

        var result = await Client().ChangeDefaultAsync(
            TenantId, TemplateId, new UpdateReportingExportDefaultRequest { Mode = ReportingExportModeSetting.ExportAll }, "ada", CancellationToken.None);

        Assert.Equal(DomainErrorCode.Validation, result.ErrorCode);
        Assert.Equal("A reason is required to export everything.", result.Error);
    }

    [Fact]
    public async Task GetRefresh_ShouldHideOtherTenantsOperations()
    {
        var refreshId = Guid.NewGuid();
        _handler.Respond(HttpStatusCode.OK, $$"""
            {"operationId":"{{refreshId}}","tenantId":"{{Guid.NewGuid()}}","status":"Running","applicationsScanned":5,
             "messagesEnqueued":2,"error":null,"createdAt":"2026-10-05T10:00:00Z","startedAt":null,"completedAt":null}
            """);

        var result = await Client().GetRefreshAsync(TenantId, refreshId, CancellationToken.None);

        Assert.Equal(DomainErrorCode.NotFound, result.ErrorCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, PrismExportControlClient.NotAllowed)]
    [InlineData(HttpStatusCode.Forbidden, PrismExportControlClient.NotAllowed)]
    [InlineData(HttpStatusCode.InternalServerError, PrismExportControlClient.Unavailable)]
    public async Task Failures_ShouldGiveAdminFacingMessages(HttpStatusCode status, string message)
    {
        _handler.Respond(status, "");

        var result = await Client().GetDefaultAsync(TenantId, null, CancellationToken.None);

        Assert.Equal(DomainErrorCode.BadRequest, result.ErrorCode);
        Assert.Equal(message, result.Error);
    }

    [Fact]
    public async Task Unreachable_ShouldReportUnavailable()
    {
        _handler.Throw(new HttpRequestException("refused"));

        var result = await Client().GetDefaultAsync(TenantId, TemplateId, CancellationToken.None);

        Assert.Equal(PrismExportControlClient.Unavailable, result.Error);
    }

    [Fact]
    public async Task ShouldUseBearerToken_WhenNoDevelopmentKey()
    {
        _options.DevelopmentKey = null;
        _options.Scope = "api://prism/.default";
        _tokens.GetTokenAsync("api://prism/.default", Arg.Any<CancellationToken>()).Returns("token-123");
        _handler.Respond(HttpStatusCode.NotFound, "");

        await Client().GetDefaultAsync(TenantId, null, CancellationToken.None);

        Assert.Equal("Bearer token-123", _handler.Request!.Headers.Authorization!.ToString());
        Assert.False(_handler.Request.Headers.Contains(PrismExportControlClient.DevelopmentKeyHeader));
    }

    [Theory]
    [InlineData("", "dev-key", null)]
    [InlineData("http://prism.local/", null, null)]
    public async Task ShouldReportNotConfigured_WithoutBaseUrlOrCredentials(string baseUrl, string? developmentKey, string? scope)
    {
        _options.BaseUrl = baseUrl;
        _options.DevelopmentKey = developmentKey;
        _options.Scope = scope;

        var result = await Client().GetDefaultAsync(TenantId, null, CancellationToken.None);

        Assert.Equal(PrismExportControlClient.NotConfigured, result.Error);
        Assert.Null(_handler.Request);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private Func<HttpResponseMessage> _respond = () => new HttpResponseMessage(HttpStatusCode.OK);

        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        public void Respond(HttpStatusCode status, string json) =>
            _respond = () => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        public void Throw(Exception exception) => _respond = () => throw exception;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return _respond();
        }
    }
}
