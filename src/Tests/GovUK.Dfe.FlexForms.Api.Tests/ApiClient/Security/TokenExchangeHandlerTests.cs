using System.Net;
using System.Security.Claims;
using System.Text.Json;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.FlexForms.Api.Client.Contracts;
using GovUK.Dfe.FlexForms.Api.Client.Security;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using static GovUK.Dfe.FlexForms.Api.Tests.ApiClient.Security.ApiClientTestKit;

namespace GovUK.Dfe.FlexForms.Api.Tests.ApiClient.Security;

public class TokenExchangeHandlerTests
{
    private readonly IInternalUserTokenStore _tokenStore = Substitute.For<IInternalUserTokenStore>();
    private readonly ITokensClient _tokensClient = Substitute.For<ITokensClient>();
    private readonly ITokenAcquisitionService _azure = Substitute.For<ITokenAcquisitionService>();
    private readonly ITokenStateManager _state = Substitute.For<ITokenStateManager>();
    private readonly RecordingHandler _api = new();
    private Microsoft.AspNetCore.Http.HttpContext? _context = SignedIn(new Claim(ClaimTypes.Name, "bob@example.com"));

    public TokenExchangeHandlerTests()
    {
        _azure.GetTokenAsync().Returns("azure-token");
    }

    [Fact]
    public async Task ValidOboToken_IsSentAsTheBearer()
    {
        State(obo: Valid("obo-token"));

        var response = await SendAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Bearer obo-token", Assert.Single(_api.Requests).Headers.Authorization!.ToString());
        await _tokensClient.DidNotReceiveWithAnyArgs().ExchangeAsync(default!, default);
    }

    [Fact]
    public async Task NoOboToken_ExchangesTheIdpTokenAndStoresTheResult()
    {
        State(idp: Valid("idp-token"));
        _tokensClient.ExchangeAsync(Arg.Is<ExchangeTokenRequest>(r => r.AccessToken == "idp-token"), Arg.Any<CancellationToken>())
            .Returns(new ExchangeTokenDto { AccessToken = "new-obo" });

        var response = await SendAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Bearer new-obo", Assert.Single(_api.Requests).Headers.Authorization!.ToString());
        _tokenStore.Received(1).SetToken("new-obo");
    }

    [Fact]
    public async Task RefreshableState_IsRefreshedAndReadAgainBeforeExchanging()
    {
        State(canRefresh: true);
        _state.RefreshTokensIfPossibleAsync().Returns(true);
        _state.GetCurrentTokenStateAsync().Returns(
            new TokenState { IsAuthenticated = true, CanRefresh = true },
            new TokenState { IsAuthenticated = true, ExternalIdpToken = Valid("fresh-idp") });
        _tokensClient.ExchangeAsync(default!, default).ReturnsForAnyArgs(new ExchangeTokenDto { AccessToken = "obo" });

        await SendAsync();

        await _tokensClient.Received(1).ExchangeAsync(Arg.Is<ExchangeTokenRequest>(r => r.AccessToken == "fresh-idp"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForcedLogoutState_LogsOutAndAnswers401WithoutCallingTheApi()
    {
        State();
        _state.ShouldForceLogout(Arg.Any<TokenState>()).Returns(true);

        var response = await SendAsync();

        await AssertUnauthorizedAsync(response);
        await _state.Received(1).ForceCompleteLogoutAsync();
    }

    [Fact]
    public async Task NoUsableIdpToken_LogsOutAndAnswers401()
    {
        State(idp: new TokenInfo { Value = "expired", ExpiryTime = DateTime.UtcNow.AddMinutes(-1) });

        await AssertUnauthorizedAsync(await SendAsync());
        await _state.Received(1).ForceCompleteLogoutAsync();
    }

    [Theory]
    [InlineData(403, "User is not a member of tenant 'x'")]
    [InlineData(404, "User not found for email bob@example.com")]
    public async Task ExchangeRefusedForTenantAccess_Answers403WithTheMembershipErrorAndDoesNotLogOut(int status, string body)
    {
        State(idp: Valid("idp-token"));
        _tokensClient.ExchangeAsync(default!, default)
            .ThrowsAsyncForAnyArgs(new ExternalApplicationsException("refused", status, body, new Dictionary<string, IEnumerable<string>>(), null));

        var response = await SendAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = await ReadErrorAsync(response);
        Assert.Equal(TokenExchangeErrors.NoTenantMembershipErrorId, error.ErrorId);
        Assert.Equal(TokenExchangeErrors.NoTenantMembershipMessage, error.Message);
        _tokenStore.Received(1).ClearToken();
        await _state.DidNotReceive().ForceCompleteLogoutAsync();
        Assert.Empty(_api.Requests);
    }

    [Theory]
    [InlineData(401, "Invalid token")]
    [InlineData(403, "Forbidden")]
    [InlineData(500, "boom")]
    public async Task OtherExchangeFailures_LogOutAndAnswer401(int status, string body)
    {
        State(idp: Valid("idp-token"));
        _tokensClient.ExchangeAsync(default!, default)
            .ThrowsAsyncForAnyArgs(new ExternalApplicationsException("failed", status, body, new Dictionary<string, IEnumerable<string>>(), null));

        await AssertUnauthorizedAsync(await SendAsync());
        await _state.Received(1).ForceCompleteLogoutAsync();
    }

    [Fact]
    public async Task ExchangeWithoutAnAzureToken_LogsOutAndAnswers401()
    {
        State(idp: Valid("idp-token"));
        _azure.GetTokenAsync().Returns("");

        await AssertUnauthorizedAsync(await SendAsync());
        await _tokensClient.DidNotReceiveWithAnyArgs().ExchangeAsync(default!, default);
    }

    [Fact]
    public async Task ExchangeReturningNoAccessToken_LogsOutAndAnswers401()
    {
        State(idp: Valid("idp-token"));
        _tokensClient.ExchangeAsync(default!, default).ReturnsForAnyArgs(new ExchangeTokenDto());

        await AssertUnauthorizedAsync(await SendAsync());
        _tokenStore.DidNotReceiveWithAnyArgs().SetToken(default!);
    }

    [Fact]
    public async Task SignedOutOrNoHttpContext_Answers401WithoutReadingTokenState()
    {
        _context = SignedOut();
        await AssertUnauthorizedAsync(await SendAsync());

        _context = null;
        await AssertUnauthorizedAsync(await SendAsync());

        await _state.DidNotReceive().GetCurrentTokenStateAsync();
    }

    [Fact]
    public async Task StateManagerThrows_Answers401()
    {
        _state.GetCurrentTokenStateAsync().ThrowsAsync(new InvalidOperationException("cache down"));

        await AssertUnauthorizedAsync(await SendAsync());
    }

    [Fact]
    public async Task ClientAbortedTheRequest_IsCancelledNotTreatedAsAnAuthFailure()
    {
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        _context!.RequestAborted = aborted.Token;
        _state.GetCurrentTokenStateAsync().ThrowsAsync(new TaskCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(SendAsync);
    }

    [Fact]
    public async Task ServiceToServiceScope_UsesTheAzureTokenAndSkipsTheUserFlow()
    {
        _context = null;
        using (AuthenticationContext.UseServiceToServiceAuthScope())
        {
            await SendAsync();
        }

        Assert.Equal("Bearer azure-token", Assert.Single(_api.Requests).Headers.Authorization!.ToString());
        await _state.DidNotReceive().GetCurrentTokenStateAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServiceToServiceScope_WithoutAnAzureTokenAnswers401(bool throws)
    {
        if (throws)
            _azure.GetTokenAsync().ThrowsAsync(new InvalidOperationException("no identity"));
        else
            _azure.GetTokenAsync().Returns("");

        using (AuthenticationContext.UseServiceToServiceAuthScope())
        {
            await AssertUnauthorizedAsync(await SendAsync());
        }
    }

    private void State(TokenInfo? obo = null, TokenInfo? idp = null, bool canRefresh = false) =>
        _state.GetCurrentTokenStateAsync().Returns(new TokenState
        {
            IsAuthenticated = true,
            OboToken = obo ?? new TokenInfo(),
            ExternalIdpToken = idp ?? new TokenInfo(),
            CanRefresh = canRefresh
        });

    private static TokenInfo Valid(string value) => new() { Value = value, ExpiryTime = DateTime.UtcNow.AddHours(1) };

    private Task<HttpResponseMessage> SendAsync()
    {
        var handler = new TokenExchangeHandler(
            Accessor(_context), _tokenStore, _tokensClient, _azure, _state, NullLogger<TokenExchangeHandler>.Instance);
        return RecordingHandler.SendThrough(handler, _api, new HttpRequestMessage(HttpMethod.Get, "https://api.example.gov.uk/v1/applications"));
    }

    private async Task AssertUnauthorizedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Exchange-401", (await ReadErrorAsync(response)).ErrorId);
        Assert.Empty(_api.Requests);
    }

    private static async Task<ExceptionResponse> ReadErrorAsync(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<ExceptionResponse>(await response.Content.ReadAsStringAsync())!;
}
