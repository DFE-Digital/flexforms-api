using System.Net;
using System.Text;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.FlexForms.Infrastructure.Configurations;
using GovUK.Dfe.FlexForms.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class PrismReportingExportPolicyServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid TemplateId = Guid.Parse("33333333-3333-4333-8333-333333333333");

    private readonly StubHandler _handler = new();
    private readonly IPrismAccessTokenSource _tokens = Substitute.For<IPrismAccessTokenSource>();
    private readonly PrismControlApiOptions _options = new() { BaseUrl = "http://prism.local/", DevelopmentKey = "dev-key" };

    private PrismReportingExportPolicyService Service() =>
        new(new HttpClient(_handler), Microsoft.Extensions.Options.Options.Create(_options), _tokens, NullLogger<PrismReportingExportPolicyService>.Instance);

    [Fact]
    public async Task GetPolicy_ShouldMapPrismView()
    {
        _handler.Respond(HttpStatusCode.OK, $$"""
            {"tenantId":"{{TenantId}}","templateId":"{{TemplateId}}","policyVersion":7,"defaultMode":"ExportAll","defaultSource":"Template",
             "fields":[{"parentFieldId":"","fieldId":"email","label":"Email","dataType":"Text","isCollection":false,"taskName":"Contact",
                        "pageTitle":"Your details","templateVersionNumber":"1.2","exportStatus":"AllowedByDefault","reason":null,
                        "decidedBy":null,"decidedAt":null,"policyVersion":null}]}
            """);

        var policy = await Service().GetPolicyAsync(TenantId, TemplateId);

        Assert.Equal(7, policy!.PolicyVersion);
        Assert.Equal(ReportingExportMode.ExportAll, policy.DefaultMode);
        Assert.Equal(ReportingExportDefaultSource.Template, policy.DefaultSource);
        var field = Assert.Single(policy.Fields);
        Assert.Equal(ReportingExportStatus.AllowedByDefault, field.Status);
        Assert.Equal("Your details", field.PageTitle);
        Assert.Equal($"http://prism.local/api/control/tenants/{TenantId}/templates/{TemplateId}/export-policy", _handler.Request!.RequestUri!.ToString());
        Assert.Equal("dev-key", _handler.Request.Headers.GetValues(PrismReportingExportPolicyService.DevelopmentKeyHeader).Single());
    }

    [Fact]
    public async Task GetPolicy_ShouldReturnNull_WhenTemplateNotCatalogued()
    {
        _handler.Respond(HttpStatusCode.NotFound, "");

        Assert.Null(await Service().GetPolicyAsync(TenantId, TemplateId));
    }

    [Fact]
    public async Task ChangeDecisions_ShouldSendActingUserAndMapRefresh()
    {
        var operationId = Guid.NewGuid();
        _handler.Respond(HttpStatusCode.OK, $$"""
            {"result":{"status":"Applied","policyVersion":8,"changed":1,"problems":[],"warnings":["Email is personal data"]},
             "backfill":{"operationId":"{{operationId}}","tenantId":"{{TenantId}}","status":"Pending","applicationsScanned":0,
                         "messagesEnqueued":0,"error":null,"createdAt":"2026-10-05T10:00:00Z","startedAt":null,"completedAt":null} }
            """);

        var change = await Service().ChangeDecisionsAsync(
            TenantId,
            TemplateId,
            [new ReportingExportDecisionRequest { FieldId = "email", Decision = ReportingExportDecision.Allowed, Reason = "Needed" }],
            "ada@example.gov.uk");

        Assert.Equal(ReportingExportChangeStatus.Applied, change!.Status);
        Assert.Equal(operationId, change.RefreshId);
        Assert.Equal(["Email is personal data"], change.Warnings);
        Assert.Equal(HttpMethod.Put, _handler.Request!.Method);
        Assert.Equal("ada@example.gov.uk", _handler.Request.Headers.GetValues(PrismReportingExportPolicyService.ActingUserHeader).Single());
        Assert.Contains("\"decision\":\"Allowed\"", _handler.Body);
        Assert.Contains("\"fieldId\":\"email\"", _handler.Body);
    }

    [Fact]
    public async Task ChangeDecisions_ShouldThrowArgumentListingUnknownFields_OnUnprocessableEntity()
    {
        _handler.Respond(HttpStatusCode.UnprocessableEntity, """
            {"result":{"status":"UnknownFields","policyVersion":3,"changed":0,"problems":["phone","fax"],"warnings":[]},"backfill":null}
            """);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Service().ChangeDecisionsAsync(TenantId, TemplateId, [], "ada"));

        Assert.Equal("These fields are not in the template: phone, fax.", ex.Message);
    }

    [Fact]
    public async Task ChangeDefault_ShouldUseTenantPath_WithoutTemplate()
    {
        _handler.Respond(HttpStatusCode.OK, """{"result":{"status":"Unchanged","policyVersion":2,"default":null,"problems":[]},"backfill":null}""");

        var change = await Service().ChangeDefaultAsync(
            TenantId, null, new UpdateReportingExportDefaultRequest { Mode = ReportingExportModeSetting.ApproveFirst }, "ada");

        Assert.Equal(ReportingExportChangeStatus.Unchanged, change!.Status);
        Assert.Equal(0, change.Changed);
        Assert.Null(change.RefreshId);
        Assert.Equal($"http://prism.local/api/control/tenants/{TenantId}/export-default", _handler.Request!.RequestUri!.ToString());
        Assert.Contains("\"mode\":\"ApproveFirst\"", _handler.Body);
    }

    [Fact]
    public async Task ChangeDefault_ShouldThrowArgumentWithProblemDetail_OnBadRequest()
    {
        _handler.Respond(HttpStatusCode.BadRequest, """{"status":400,"detail":"A reason is required to export everything."}""");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Service().ChangeDefaultAsync(
            TenantId, TemplateId, new UpdateReportingExportDefaultRequest { Mode = ReportingExportModeSetting.ExportAll }, "ada"));

        Assert.Equal("A reason is required to export everything.", ex.Message);
    }

    [Fact]
    public async Task GetRefresh_ShouldReturnNull_ForAnotherTenantsRefresh()
    {
        var refreshId = Guid.NewGuid();
        _handler.Respond(HttpStatusCode.OK, $$"""
            {"operationId":"{{refreshId}}","tenantId":"{{Guid.NewGuid()}}","status":"Running","applicationsScanned":5,
             "messagesEnqueued":2,"error":null,"createdAt":"2026-10-05T10:00:00Z","startedAt":null,"completedAt":null}
            """);

        Assert.Null(await Service().GetRefreshAsync(TenantId, refreshId));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, PrismReportingExportPolicyService.NotAllowed)]
    [InlineData(HttpStatusCode.Forbidden, PrismReportingExportPolicyService.NotAllowed)]
    [InlineData(HttpStatusCode.InternalServerError, PrismReportingExportPolicyService.Unavailable)]
    public async Task Failures_ShouldThrowInvalidOperationWithAdminFacingMessage(HttpStatusCode status, string message)
    {
        _handler.Respond(status, "");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Service().GetDefaultAsync(TenantId, null));

        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public async Task Unreachable_ShouldThrowInvalidOperation()
    {
        _handler.Throw(new HttpRequestException("refused"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Service().GetDefaultAsync(TenantId, TemplateId));

        Assert.Equal(PrismReportingExportPolicyService.Unavailable, ex.Message);
    }

    [Fact]
    public async Task ShouldUseBearerToken_WhenNoDevelopmentKey()
    {
        _options.DevelopmentKey = null;
        _options.Scope = "api://prism/.default";
        _tokens.GetTokenAsync("api://prism/.default", Arg.Any<CancellationToken>()).Returns("token-123");
        _handler.Respond(HttpStatusCode.NotFound, "");

        await Service().GetDefaultAsync(TenantId, null);

        Assert.Equal("Bearer token-123", _handler.Request!.Headers.Authorization!.ToString());
        Assert.False(_handler.Request.Headers.Contains(PrismReportingExportPolicyService.DevelopmentKeyHeader));
    }

    [Theory]
    [InlineData("", "dev-key", null)]
    [InlineData("http://prism.local/", null, null)]
    public async Task ShouldThrowNotConfigured_WithoutBaseUrlOrCredentials(string baseUrl, string? developmentKey, string? scope)
    {
        _options.BaseUrl = baseUrl;
        _options.DevelopmentKey = developmentKey;
        _options.Scope = scope;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Service().GetDefaultAsync(TenantId, null));

        Assert.Equal(PrismReportingExportPolicyService.NotConfigured, ex.Message);
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
