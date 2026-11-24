using System.Diagnostics.CodeAnalysis;
using System.Text;
using GovUK.Dfe.FlexForms.Api.Client.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static GovUK.Dfe.FlexForms.Api.Tests.ApiClient.Security.ApiClientTestKit;

namespace GovUK.Dfe.FlexForms.Api.Tests.ApiClient.Security;

public class HeaderForwardingHandlerTests
{
    private const string Correlation = HeaderForwardingHandler.CorrelationIdHeaderName;
    private readonly RecordingHandler _api = new();

    [Fact]
    public async Task TenantHeader_IsAlwaysTheConfiguredTenant()
    {
        var request = Request();
        request.Headers.Add(HeaderForwardingHandler.TenantIdHeaderName, TenantB.ToString());

        await SendAsync(new DefaultHttpContext(), request);

        Assert.Equal([TenantA.ToString()], Sent().Headers.GetValues(HeaderForwardingHandler.TenantIdHeaderName));
    }

    [Fact]
    public async Task NoTenantConfigured_SendsNoTenantHeader()
    {
        await SendAsync(new DefaultHttpContext(), Request(), Settings(configure: s => s.TenantId = null));

        Assert.False(Sent().Headers.Contains(HeaderForwardingHandler.TenantIdHeaderName));
    }

    [Fact]
    public async Task IncomingCorrelationId_IsForwarded()
    {
        var correlation = Guid.NewGuid().ToString();
        var context = new DefaultHttpContext();
        context.Request.Headers[Correlation] = correlation;

        await SendAsync(context, Request());

        Assert.Equal([correlation], Sent().Headers.GetValues(Correlation));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public async Task MissingOrInvalidCorrelationId_IsReplacedAndEchoedOnTheResponse(string? incoming)
    {
        var context = new DefaultHttpContext();
        if (incoming is not null)
            context.Request.Headers[Correlation] = incoming;

        await SendAsync(context, Request());

        var sent = Sent().Headers.GetValues(Correlation).Single();
        Assert.True(Guid.TryParse(sent, out _));
        Assert.Equal(sent, context.Response.Headers[Correlation].ToString());
    }

    [Fact]
    public async Task NoHttpContext_StillSendsACorrelationId()
    {
        await SendAsync(null, Request());

        Assert.True(Guid.TryParse(Sent().Headers.GetValues(Correlation).Single(), out _));
    }

    [Fact]
    public async Task DefaultHeaders_ForwardTheInternalServiceCredentials()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["x-service-email"] = "svc@service.com";
        context.Request.Headers["x-service-api-key"] = "key";
        context.Request.Headers["x-other"] = "not forwarded";

        await SendAsync(context, Request());

        Assert.Equal(["svc@service.com"], Sent().Headers.GetValues("x-service-email"));
        Assert.Equal(["key"], Sent().Headers.GetValues("x-service-api-key"));
        Assert.False(Sent().Headers.Contains("x-other"));
    }

    [Fact]
    public async Task ConfiguredHeaders_ReplaceTheDefaults()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["x-service-email"] = "svc@service.com";
        context.Request.Headers["x-custom"] = "value";
        context.Request.Headers["x-empty"] = "";
        var request = Request();
        request.Headers.Add("x-custom", "stale");

        await SendAsync(context, request, Settings(configure: s => s.HeadersToForward = ["x-custom", "x-empty", Correlation]));

        Assert.Equal(["value"], Sent().Headers.GetValues("x-custom"));
        Assert.False(Sent().Headers.Contains("x-service-email"));
        Assert.False(Sent().Headers.Contains("x-empty"));
    }

    [Fact]
    public async Task TemplateAndApplicationTelemetry_ComeFromTheRequestOrTheSession()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[HeaderForwardingHandler.TemplateIdHeaderName] = "template-from-header";
        var session = new TestSession();
        session.SetString("TemplateId", "template-from-session");
        session.SetString("ApplicationReference", "APP-1");
        context.Features.Set<ISessionFeature>(new SessionFeature { Session = session });

        await SendAsync(context, Request());

        Assert.Equal(["template-from-header"], Sent().Headers.GetValues(HeaderForwardingHandler.TemplateIdHeaderName));
        Assert.Equal(["APP-1"], Sent().Headers.GetValues(HeaderForwardingHandler.ApplicationReferenceHeaderName));
    }

    [Fact]
    public async Task NoSession_SendsNoTelemetryHeaders()
    {
        await SendAsync(new DefaultHttpContext(), Request());

        Assert.False(Sent().Headers.Contains(HeaderForwardingHandler.TemplateIdHeaderName));
        Assert.False(Sent().Headers.Contains(HeaderForwardingHandler.ApplicationReferenceHeaderName));
    }

    private HttpRequestMessage Sent() => Assert.Single(_api.Requests);

    private static HttpRequestMessage Request() => new(HttpMethod.Get, "https://api.example.gov.uk/v1/applications");

    private Task<HttpResponseMessage> SendAsync(
        HttpContext? context,
        HttpRequestMessage request,
        GovUK.Dfe.FlexForms.Api.Client.Settings.IApiClientSettingsProvider? settings = null) =>
        RecordingHandler.SendThrough(
            new HeaderForwardingHandler(Accessor(context), settings ?? Settings(), NullLogger<HeaderForwardingHandler>.Instance),
            _api,
            request);

    private sealed class SessionFeature : ISessionFeature
    {
        public ISession Session { get; set; } = null!;
    }

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = [];

        public bool IsAvailable => true;
        public string Id => "session-1";
        public IEnumerable<string> Keys => _values.Keys;

        public void Clear() => _values.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _values.Remove(key);
        public void Set(string key, byte[] value) => _values[key] = value;
        public bool TryGetValue(string key, [NotNullWhen(true)] out byte[]? value) => _values.TryGetValue(key, out value);
    }
}
