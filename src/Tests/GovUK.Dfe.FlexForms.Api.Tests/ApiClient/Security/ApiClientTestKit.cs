using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using GovUK.Dfe.FlexForms.Api.Client.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Api.Tests.ApiClient.Security;

/// <summary>Stubs shared by the Api.Client security tests.</summary>
internal static class ApiClientTestKit
{
    public static readonly Guid TenantA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid TenantB = Guid.Parse("22222222-2222-4222-8222-222222222222");

    public static IApiClientSettingsProvider Settings(Guid? tenantId = null, Action<ApiClientSettings>? configure = null)
    {
        var settings = new ApiClientSettings { TenantId = tenantId ?? TenantA, BaseUrl = "https://api.example.gov.uk" };
        configure?.Invoke(settings);
        var provider = Substitute.For<IApiClientSettingsProvider>();
        provider.GetSettings().Returns(settings);
        return provider;
    }

    public static IDistributedCache MemoryCache() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    public static IHttpContextAccessor Accessor(HttpContext? context)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(context);
        return accessor;
    }

    public static DefaultHttpContext SignedIn(params Claim[] claims) =>
        new() { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuthentication", ClaimTypes.Name, ClaimTypes.Role)) };

    public static DefaultHttpContext SignedOut() => new() { User = new ClaimsPrincipal(new ClaimsIdentity()) };

    /// <summary>An unsigned JWT; the code under test only reads it.</summary>
    public static string Jwt(TimeSpan validFor, string issuer = "https://login.education.gov.uk", DateTime? issuedAt = null)
    {
        var now = issuedAt ?? DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: "flexforms",
            claims: [new Claim("sub", Guid.NewGuid().ToString())],
            notBefore: now.AddMinutes(-1),
            expires: now.Add(validFor));
        token.Payload["iat"] = new DateTimeOffset(now).ToUnixTimeSeconds();
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>The end of a handler chain: records each request and answers with <see cref="Respond"/>.</summary>
internal sealed class RecordingHandler : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string?> Bodies { get; } = [];

    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
        var response = Respond(request);
        response.RequestMessage ??= request;
        return response;
    }

    public static Task<HttpResponseMessage> SendThrough(DelegatingHandler handler, HttpMessageHandler inner, HttpRequestMessage request)
    {
        handler.InnerHandler = inner;
        return new HttpMessageInvoker(handler).SendAsync(request, CancellationToken.None);
    }
}
