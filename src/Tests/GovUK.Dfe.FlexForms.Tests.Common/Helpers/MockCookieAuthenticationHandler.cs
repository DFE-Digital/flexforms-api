using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace GovUK.Dfe.FlexForms.Tests.Common.Helpers;

/// <summary>
/// Mock cookie authentication handler that supports SignInAsync for SignalR hub authentication in tests.
/// Like the real cookie handler, the claims passed to SignInAsync round-trip through the cookie, so the hub
/// sees the signed-in email and tenant claims.
/// </summary>
public class MockCookieAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>, IAuthenticationSignInHandler
{
    private const string CookieName = "test-auth-cookie";

    public MockCookieAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock)
        : base(options, logger, encoder, clock)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var cookieHeader = Request.Headers["Cookie"].FirstOrDefault();
        if (string.IsNullOrEmpty(cookieHeader))
        {
            return Task.FromResult(AuthenticateResult.Fail("No authentication cookie provided"));
        }

        var cookieValue = cookieHeader
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(parts => parts.Length == 2 && parts[0] == CookieName)
            .Select(parts => parts[1])
            .FirstOrDefault();

        if (string.IsNullOrEmpty(cookieValue))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid authentication cookie"));
        }

        Dictionary<string, string>? storedClaims;
        try
        {
            storedClaims = JsonSerializer.Deserialize<Dictionary<string, string>>(
                Encoding.UTF8.GetString(Convert.FromBase64String(Uri.UnescapeDataString(cookieValue))));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid authentication cookie"));
        }

        var claims = (storedClaims ?? new Dictionary<string, string>())
            .Select(kvp => new Claim(kvp.Key, kvp.Value))
            .ToList();

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 403;
        return Task.CompletedTask;
    }

    public Task SignInAsync(ClaimsPrincipal user, AuthenticationProperties? properties)
    {
        var claims = user.Claims
            .GroupBy(c => c.Type)
            .ToDictionary(g => g.Key, g => g.First().Value);

        var encoded = Uri.EscapeDataString(
            Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(claims))));

        Response.Headers.Add("Set-Cookie", $"{CookieName}={encoded}; Path=/; HttpOnly");
        return Task.CompletedTask;
    }

    public Task SignOutAsync(AuthenticationProperties? properties)
    {
        // Mock implementation - in a real scenario this would clear the authentication cookie
        // For tests, we'll just return success
        return Task.CompletedTask;
    }
}
