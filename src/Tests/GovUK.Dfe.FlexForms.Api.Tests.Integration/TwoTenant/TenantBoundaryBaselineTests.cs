using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Tests.Common.TwoTenant;

namespace GovUK.Dfe.FlexForms.Api.Tests.Integration.TwoTenant;

/// <summary>
/// The tenant boundary with real tokens: a credential issued for one tenant is refused by the other at
/// authentication (401), and a request without credentials is a real 401. Runs once with distinct
/// signing settings per tenant and once with one shared key, where <c>tenant_id</c> is the only barrier.
/// </summary>
public abstract class TenantBoundaryBaselineTests(TwoTenantApi api)
{
    private static readonly TestTenant A = TestTenant.A;
    private static readonly TestTenant B = TestTenant.B;

    [Fact]
    public async Task ATokenWorksForTheTenantThatIssuedIt()
    {
        var token = await api.SignInAsync(A, A.OwnerEmail);
        using var client = api.Client(A, token);

        using var permissions = await client.GetAsync("v1/me/permissions");
        using var applications = await client.GetAsync("v1/me/applications");

        Assert.Equal(HttpStatusCode.OK, permissions.StatusCode);
        Assert.Equal(HttpStatusCode.OK, applications.StatusCode);
        Assert.Contains(A.ApplicationReference, await applications.Content.ReadAsStringAsync());
        Assert.Equal(A.Id.ToString(), TenantIdClaim(token));
    }

    [Theory]
    [InlineData("v1/me/permissions")]
    [InlineData("v1/me/applications")]
    public async Task ATokenIssuedForTenantAIsRefusedByTenantB(string path)
    {
        var token = await api.SignInAsync(A, A.OwnerEmail);
        using var client = api.Client(B, token);

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AUserInBothTenantsMustSignInToEachSeparately()
    {
        var tokenA = await api.SignInAsync(A, TwoTenantSeeder.BothTenantsEmail);
        var tokenB = await api.SignInAsync(B, TwoTenantSeeder.BothTenantsEmail);

        using var aOnB = await api.Client(B, tokenA).GetAsync("v1/me/permissions");
        using var bOnA = await api.Client(A, tokenB).GetAsync("v1/me/permissions");
        using var bOnB = await api.Client(B, tokenB).GetAsync("v1/me/permissions");

        Assert.Equal(HttpStatusCode.Unauthorized, aOnB.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, bOnA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bOnB.StatusCode);
    }

    [Fact]
    public async Task AServiceApiKeyForTenantAIsRefusedByTenantB()
    {
        // The same exchange request, changing only the tenant it is sent to.
        using var ownTenant = await api.ExchangeAsync(A, TwoTenantSeeder.BothTenantsEmail, serviceApiKey: A.ServiceApiKey);
        using var otherTenant = await api.ExchangeAsync(B, TwoTenantSeeder.BothTenantsEmail, serviceApiKey: A.ServiceApiKey);

        Assert.Equal(HttpStatusCode.OK, ownTenant.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, otherTenant.StatusCode);
    }

    [Fact]
    public async Task ExchangeRefusesAUserWhoIsNotAMemberOfTheTenant()
    {
        using var notAMember = await api.ExchangeAsync(B, A.OwnerEmail);
        using var unknownKey = await api.ExchangeAsync(A, A.OwnerEmail, serviceApiKey: "not-a-configured-key");
        using var noKey = await api.Client(A).PostAsJsonAsync("v1/tokens/exchange", new { AccessToken = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, notAMember.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownKey.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, noKey.StatusCode);
    }

    [Fact]
    public async Task APlatformSuperAdminGetsATokenPerTenant()
    {
        var tokenA = await api.SignInAsync(A, TwoTenantSeeder.SuperAdminEmail);

        using var onA = await api.Client(A, tokenA).GetAsync("v1/me/permissions");
        using var onB = await api.Client(B, tokenA).GetAsync("v1/me/permissions");

        Assert.Equal(HttpStatusCode.OK, onA.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, onB.StatusCode);
    }

    public static TheoryData<string, string> EndpointsNeedingAuthentication() => new()
    {
        { "POST", $"v1/applications/{A.ApplicationId}/responses" },
        { "POST", "v1/applications" },
        { "GET", "v1/me/applications" },
        { "GET", $"v1/applications/reference/{A.ApplicationReference}" },
        { "DELETE", $"v1/applications/{A.ApplicationId}" },
        { "POST", $"v1/applications/{A.ApplicationId}/submit" },
        { "GET", $"v1/applications/{A.ApplicationId}/contributors" },
        { "POST", $"v1/applications/{A.ApplicationId}/contributors" },
        { "DELETE", $"v1/applications/{A.ApplicationId}/contributors/{A.CaseworkerId}" },
        { "POST", $"v1/applications/{A.ApplicationId}/files" },
        { "GET", $"v1/applications/reference/{A.ApplicationReference}/preview/download" },
        { "GET", "v1/me/permissions" },
        { "GET", "v1/me/test-auth-authenticator" },
        { "GET", $"v1/templates/{A.TemplateId}/schema" },
        { "POST", $"v1/templates/{A.TemplateId}/versions" },
        { "POST", "v1/notifications" },
        { "GET", "v1/notifications" },
        { "GET", "v1/notifications/unread" },
        { "GET", "v1/notifications/unread/count" },
        { "POST", "v1/tokens/exchange" },
        { "POST", "v1/users/register" },
        { "POST", "v1/users/roles" },
        { "GET", $"v1/templates/{A.TemplateId}/custom-statuses" },
        { "POST", $"v1/templates/{A.TemplateId}/custom-statuses" },
    };

    [Theory]
    [MemberData(nameof(EndpointsNeedingAuthentication))]
    public async Task ARequestWithoutCredentialsIsUnauthorized(string method, string path)
    {
        using var client = api.Client(A);
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (path.EndsWith("/files", StringComparison.Ordinal))
            request.Content = new MultipartFormDataContent { { new StringContent("x"), "file", "x.pdf" } };
        else if (method != "GET" && method != "DELETE")
            request.Content = JsonContent.Create(new { });

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    protected static string? TenantIdClaim(string token) =>
        new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
            .FirstOrDefault(c => c.Type == TenantAuthClaimTypes.TenantId)?.Value;

    protected static (string Issuer, string Audience) IssuerAndAudience(string token)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        return (jwt.Issuer, jwt.Audiences.Single());
    }
}

/// <summary>Each tenant signs with its own key, issuer and audience.</summary>
public sealed class TenantBoundaryWithDistinctSigningTests(TwoTenantApi api)
    : TenantBoundaryBaselineTests(api), IClassFixture<TwoTenantApi>
{
    [Fact]
    public async Task TheTenantsIssueTokensWithDifferentIssuersAndAudiences()
    {
        var tokenA = await api.SignInAsync(TestTenant.A, TestTenant.A.OwnerEmail);
        var tokenB = await api.SignInAsync(TestTenant.B, TestTenant.B.OwnerEmail);

        Assert.NotEqual(IssuerAndAudience(tokenA).Issuer, IssuerAndAudience(tokenB).Issuer);
        Assert.NotEqual(IssuerAndAudience(tokenA).Audience, IssuerAndAudience(tokenB).Audience);
    }
}

/// <summary>
/// Both tenants sign with one key, issuer and audience, so tenant A's token is valid for B in every respect
/// except its <c>tenant_id</c>. The refusals in the base class therefore rest on the tenant consistency check.
/// </summary>
public sealed class TenantBoundaryWithSharedSigningTests(TwoTenantApiWithSharedSigning api)
    : TenantBoundaryBaselineTests(api), IClassFixture<TwoTenantApiWithSharedSigning>
{
    [Fact]
    public async Task TheTenantsIssueTokensThatDifferOnlyInTenantId()
    {
        var tokenA = await api.SignInAsync(TestTenant.A, TestTenant.A.OwnerEmail);
        var tokenB = await api.SignInAsync(TestTenant.B, TestTenant.B.OwnerEmail);

        Assert.Equal(IssuerAndAudience(tokenA), IssuerAndAudience(tokenB));
        Assert.Equal(TestTenant.A.Id.ToString(), TenantIdClaim(tokenA));
        Assert.Equal(TestTenant.B.Id.ToString(), TenantIdClaim(tokenB));
    }
}
