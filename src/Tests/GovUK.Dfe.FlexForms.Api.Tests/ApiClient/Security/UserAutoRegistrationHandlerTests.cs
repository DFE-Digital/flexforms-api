using System.Net;
using System.Text;
using System.Text.Json;
using GovUK.Dfe.FlexForms.Api.Client.Security;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using static GovUK.Dfe.FlexForms.Api.Tests.ApiClient.Security.ApiClientTestKit;

namespace GovUK.Dfe.FlexForms.Api.Tests.ApiClient.Security;

/// <summary>
/// A failed token exchange for a missing user or tenant membership registers the user once, then retries.
/// </summary>
public class UserAutoRegistrationHandlerTests
{
    private const string ExchangeUrl = "https://api.example.gov.uk/v1/tokens/exchange";
    private static readonly Guid DefaultTemplate = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    private readonly ITokenStateManager _state = Substitute.For<ITokenStateManager>();
    private readonly ITokenAcquisitionService _azure = Substitute.For<ITokenAcquisitionService>();
    private readonly IHttpClientFactory _clients = Substitute.For<IHttpClientFactory>();
    private readonly RecordingHandler _api = new();
    private readonly RecordingHandler _registerApi = new();
    private readonly string _idpToken = Jwt(TimeSpan.FromHours(1), issuer: "https://oidc.signin.education.gov.uk");
    private bool _autoRegister = true;

    public UserAutoRegistrationHandlerTests()
    {
        _azure.GetTokenAsync().Returns("azure-token");
        _clients.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(_registerApi));
        _registerApi.Respond = _ => Json(HttpStatusCode.OK, new { userId = Guid.NewGuid(), email = "bob@example.com" });
        _state.GetCurrentTokenStateAsync().Returns(new TokenState
        {
            IsAuthenticated = true,
            ExternalIdpToken = new TokenInfo { Value = _idpToken, ExpiryTime = DateTime.UtcNow.AddHours(1) }
        });
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "User not found for email bob@example.com")]
    [InlineData(HttpStatusCode.Forbidden, "User is not a member of tenant 'Transfers'")]
    [InlineData(HttpStatusCode.InternalServerError, "The user bob@example.com does not exist")]
    public async Task MissingUserOrMembership_RegistersThenRetriesTheExchange(HttpStatusCode status, string message)
    {
        var attempt = 0;
        _api.Respond = _ => ++attempt == 1 ? Text(status, message) : new HttpResponseMessage(HttpStatusCode.OK);

        var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, ExchangeUrl)
        {
            Content = new StringContent("""{"accessToken":"x"}""", Encoding.UTF8, "application/json")
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, _api.Requests.Count);
        Assert.Equal("""{"accessToken":"x"}""", _api.Bodies[1]);
        var register = Assert.Single(_registerApi.Requests);
        Assert.Equal("https://api.example.gov.uk/v1/Users/register", register.RequestUri!.ToString());
        Assert.Equal("Bearer azure-token", register.Headers.Authorization!.ToString());
        Assert.Equal(TenantA.ToString(), register.Headers.GetValues(UserAutoRegistrationHandler.TenantIdHeaderName).Single());
        var body = JsonSerializer.Deserialize<GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request.RegisterUserRequest>(
            _registerApi.Bodies[0]!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal(_idpToken, body.AccessToken);
        Assert.Equal(DefaultTemplate, body.TemplateId);
    }

    [Fact]
    public async Task AutoRegistrationTurnedOff_PassesTheErrorThrough()
    {
        _autoRegister = false;
        _api.Respond = _ => Text(HttpStatusCode.NotFound, "User not found");

        var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, ExchangeUrl));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(_registerApi.Requests);
    }

    [Theory]
    [InlineData("https://api.example.gov.uk/v1/applications", HttpStatusCode.NotFound, "User not found")]
    [InlineData(ExchangeUrl, HttpStatusCode.Unauthorized, "User not found")]
    [InlineData(ExchangeUrl, HttpStatusCode.Forbidden, "Forbidden")]
    [InlineData(ExchangeUrl, HttpStatusCode.NotFound, "")]
    public async Task OtherFailures_AreNotTreatedAsRegistrable(string url, HttpStatusCode status, string message)
    {
        _api.Respond = _ => Text(status, message);

        var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, url));

        Assert.Equal(status, response.StatusCode);
        Assert.Single(_api.Requests);
        Assert.Empty(_registerApi.Requests);
    }

    [Fact]
    public async Task TokenFromAnotherIssuer_IsNotRegistered()
    {
        _state.GetCurrentTokenStateAsync().Returns(new TokenState
        {
            IsAuthenticated = true,
            ExternalIdpToken = new TokenInfo { Value = Jwt(TimeSpan.FromHours(1), issuer: "https://accounts.example.com"), ExpiryTime = DateTime.UtcNow.AddHours(1) }
        });

        await AssertNotRegisteredAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-jwt")]
    public async Task MissingOrUnreadableIdpToken_IsNotRegistered(string? token)
    {
        _state.GetCurrentTokenStateAsync().Returns(new TokenState
        {
            IsAuthenticated = true,
            ExternalIdpToken = new TokenInfo { Value = token, ExpiryTime = DateTime.UtcNow.AddHours(1) }
        });

        await AssertNotRegisteredAsync();
    }

    [Fact]
    public async Task NoAzureToken_IsNotRegistered()
    {
        _azure.GetTokenAsync().Returns("");

        await AssertNotRegisteredAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RegistrationRefusedOrFailing_ReturnsTheOriginalError(bool apiRefuses)
    {
        if (apiRefuses)
            _registerApi.Respond = _ => Json(HttpStatusCode.Forbidden, new { statusCode = 403, message = "Self registration is off" });
        else
            _azure.GetTokenAsync().ThrowsAsync(new InvalidOperationException("no identity"));

        await AssertNotRegisteredAsync(expectRegisterCall: apiRefuses);
    }

    private async Task AssertNotRegisteredAsync(bool expectRegisterCall = false)
    {
        _api.Respond = _ => Text(HttpStatusCode.NotFound, "User not found");

        var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, ExchangeUrl));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Single(_api.Requests);
        Assert.Equal(expectRegisterCall ? 1 : 0, _registerApi.Requests.Count);
    }

    private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        var settings = Settings(configure: s =>
        {
            s.AutoRegisterUsers = _autoRegister;
            s.DefaultTemplateId = DefaultTemplate;
        });
        var handler = new UserAutoRegistrationHandler(_clients, _state, _azure, Accessor(null), settings, NullLogger<UserAutoRegistrationHandler>.Instance);
        return RecordingHandler.SendThrough(handler, _api, request);
    }

    private static HttpResponseMessage Text(HttpStatusCode status, string message) =>
        new(status) { Content = new StringContent(message) };

    private static HttpResponseMessage Json(HttpStatusCode status, object body) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
}
