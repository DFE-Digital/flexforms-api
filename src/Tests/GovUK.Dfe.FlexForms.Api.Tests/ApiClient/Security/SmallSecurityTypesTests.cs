using System.Net;
using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.FlexForms.Api.Client.Contracts;
using GovUK.Dfe.FlexForms.Api.Client.Security;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using static GovUK.Dfe.FlexForms.Api.Tests.ApiClient.Security.ApiClientTestKit;

namespace GovUK.Dfe.FlexForms.Api.Tests.ApiClient.Security;

public class ApiClientRequestConfigurationHandlerTests
{
    [Theory]
    [InlineData("https://api.example.gov.uk", "https://old.example/v1/applications?x=1", "https://api.example.gov.uk/v1/applications?x=1")]
    [InlineData("", "https://old.example/v1/applications", "https://old.example/v1/applications")]
    public async Task RequestUri_IsRebasedOnTheConfiguredApi(string baseUrl, string requested, string expected)
    {
        var api = new RecordingHandler();

        await RecordingHandler.SendThrough(
            new ApiClientRequestConfigurationHandler(Settings(configure: s => s.BaseUrl = baseUrl)),
            api,
            new HttpRequestMessage(HttpMethod.Get, requested));

        Assert.Equal(expected, Assert.Single(api.Requests).RequestUri!.ToString());
    }

    [Fact]
    public async Task RelativeRequestUri_IsRebasedToo()
    {
        var api = new RecordingHandler();
        var handler = new ApiClientRequestConfigurationHandler(Settings(configure: s => s.BaseUrl = "https://api.example.gov.uk"))
        {
            InnerHandler = api
        };

        await new HttpMessageInvoker(handler).SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri("v1/templates", UriKind.Relative)), CancellationToken.None);

        Assert.Equal("https://api.example.gov.uk/v1/templates", Assert.Single(api.Requests).RequestUri!.ToString());
    }
}

public class AuthenticationContextTests
{
    [Fact]
    public void ServiceScope_IsOnInsideAndRestoredAfterwards()
    {
        Assert.False(AuthenticationContext.UseServiceToServiceAuth);

        using (AuthenticationContext.UseServiceToServiceAuthScope())
        {
            Assert.True(AuthenticationContext.UseServiceToServiceAuth);
            using (AuthenticationContext.UseServiceToServiceAuthScope())
            {
                Assert.True(AuthenticationContext.UseServiceToServiceAuth);
            }

            Assert.True(AuthenticationContext.UseServiceToServiceAuth);
        }

        Assert.False(AuthenticationContext.UseServiceToServiceAuth);
    }
}

public class AzureBearerTokenHandlerTests
{
    private readonly ITokenAcquisitionService _azure = Substitute.For<ITokenAcquisitionService>();

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task AzureToken_IsSentAndTheResponseReturnedAsIs(HttpStatusCode status)
    {
        _azure.GetTokenAsync().Returns("azure-token");
        var api = new RecordingHandler { Respond = _ => new HttpResponseMessage(status) };

        var response = await SendAsync(api);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal("Bearer azure-token", Assert.Single(api.Requests).Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task EmptyAzureToken_Throws()
    {
        _azure.GetTokenAsync().Returns("");

        await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(new RecordingHandler()));
    }

    [Fact]
    public async Task AcquisitionFailure_IsRethrown()
    {
        _azure.GetTokenAsync().ThrowsAsync(new TimeoutException("identity endpoint"));

        await Assert.ThrowsAsync<TimeoutException>(() => SendAsync(new RecordingHandler()));
    }

    private Task<HttpResponseMessage> SendAsync(RecordingHandler api) =>
        RecordingHandler.SendThrough(
            new AzureBearerTokenHandler(_azure, NullLogger<AzureBearerTokenHandler>.Instance),
            api,
            new HttpRequestMessage(HttpMethod.Post, "https://api.example.gov.uk/v1/tokens/exchange"));
}

public class TokenExchangeErrorsTests
{
    [Theory]
    [InlineData(403, "User is NOT A MEMBER OF TENANT 'x'", null, true)]
    [InlineData(404, "{\"message\":\"User not found\"}", null, true)]
    [InlineData(403, "", "User is not a member of tenant 'x'", true)]
    [InlineData(404, null, "user not found for email", true)]
    [InlineData(403, "Forbidden", null, false)]
    [InlineData(404, "Template not found", null, false)]
    [InlineData(401, "not a member of tenant", null, false)]
    [InlineData(500, "user not found", null, false)]
    public void DeniedTenantAccess_IsA403NonMemberOrA404UnknownUser(int status, string? body, string? typedMessage, bool expected)
    {
        var exception = typedMessage is null
            ? new ExternalApplicationsException("x", status, body!, new Dictionary<string, IEnumerable<string>>(), null)
            : new ExternalApplicationsException<ExceptionResponse>(
                "x", status, body!, new Dictionary<string, IEnumerable<string>>(), new ExceptionResponse { Message = typedMessage }, null);

        Assert.Equal(expected, TokenExchangeErrors.IsDeniedTenantAccess(exception));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("User is not a member of tenant 'x'", true)]
    public void NotTenantMemberResponse_MatchesTheApiMessage(string? body, bool expected)
    {
        Assert.Equal(expected, TokenExchangeErrors.IsNotTenantMemberResponse(body));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("User Not Found", true)]
    public void UserNotFoundResponse_MatchesTheApiMessage(string? body, bool expected)
    {
        Assert.Equal(expected, TokenExchangeErrors.IsUserNotFoundResponse(body));
    }
}

public class TokenStateTests
{
    [Fact]
    public void TokenWithinFiveMinutesOfExpiry_CountsAsExpired()
    {
        var nearly = new TokenInfo { Value = "t", ExpiryTime = DateTime.UtcNow.AddMinutes(4) };
        var fine = new TokenInfo { Value = "t", ExpiryTime = DateTime.UtcNow.AddMinutes(30) };
        var noExpiry = new TokenInfo { Value = "t" };

        Assert.True(nearly.IsExpired);
        Assert.False(nearly.IsValid);
        Assert.True(fine.IsValid);
        Assert.True(fine.TimeUntilExpiry > TimeSpan.FromMinutes(29));
        Assert.True(noExpiry.IsValid);
        Assert.Null(noExpiry.TimeUntilExpiry);
        Assert.False(new TokenInfo().IsPresent);
    }

    [Fact]
    public void State_LogsOutOnlyWhenATokenIsExpiredAndCannotRefresh()
    {
        var soon = DateTime.UtcNow.AddMinutes(1);
        var later = DateTime.UtcNow.AddHours(1);
        var state = new TokenState
        {
            ExternalIdpToken = new TokenInfo { Value = "idp", ExpiryTime = later },
            OboToken = new TokenInfo { Value = "obo", ExpiryTime = soon }
        };

        Assert.True(state.IsAnyTokenExpired);
        Assert.True(state.ShouldLogout);
        Assert.Equal(soon, state.EarliestExpiry);

        state.CanRefresh = true;
        Assert.False(state.ShouldLogout);
        Assert.Null(new TokenState().EarliestExpiry);
    }
}

public class TokenAcquisitionServiceTests
{
    [Fact]
    public async Task MissingSettings_AreRefused()
    {
        var provider = Substitute.For<GovUK.Dfe.FlexForms.Api.Client.Settings.IApiClientSettingsProvider>();
        provider.GetSettings().Returns((GovUK.Dfe.FlexForms.Api.Client.Settings.ApiClientSettings)null!);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(provider).GetTokenAsync());

        Assert.Equal("API client settings are not available.", ex.Message);
    }

    [Theory]
    [InlineData(null, "c", "s", "https://login.example", "ExternalApplicationsApiClient:Scope is not configured.")]
    [InlineData("api://x/.default", null, "s", "https://login.example", "ExternalApplicationsApiClient:ClientId is not configured.")]
    [InlineData("api://x/.default", "c", " ", "https://login.example", "ExternalApplicationsApiClient:ClientSecret is not configured.")]
    [InlineData("api://x/.default", "c", "s", "", "ExternalApplicationsApiClient:Authority is not configured.")]
    public async Task ClientCredentials_NeedEveryValueBeforeCallingEntra(
        string? scope, string? clientId, string? secret, string? authority, string expected)
    {
        var settings = Settings(configure: s =>
        {
            s.Scope = scope;
            s.ClientId = clientId;
            s.ClientSecret = secret;
            s.Authority = authority;
            s.UseManagedIdentity = false;
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(settings).GetTokenAsync());

        Assert.Equal(expected, ex.Message);
    }

    private static TokenAcquisitionService Service(GovUK.Dfe.FlexForms.Api.Client.Settings.IApiClientSettingsProvider settings) =>
        new(settings, NullLogger<TokenAcquisitionService>.Instance);
}
