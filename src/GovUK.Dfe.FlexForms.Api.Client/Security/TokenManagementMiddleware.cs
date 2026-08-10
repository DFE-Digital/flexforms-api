using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Api.Client.Security;

/// <summary>
/// Simplified middleware that orchestrates token management
/// Single responsibility: Request interception and token state orchestration
/// </summary>
[ExcludeFromCodeCoverage]
public class TokenManagementMiddleware(RequestDelegate next, ILogger<TokenManagementMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<TokenManagementMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context, ITokenStateManager tokenStateManager)
    {
        // Only process authenticated requests
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        // Skip login/logout pages — they must not trigger token exchange/API calls.
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/TestLogin", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/TestLogout", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/Logout", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var userName = context.User.Identity.Name ?? "Unknown";

        try
        {
            // Get current token state
            var tokenState = await tokenStateManager.GetCurrentTokenStateAsync();
            // Check if refresh is allowed due to inactivity for this request
            var cache = context.RequestServices.GetService(typeof(ICacheManager)) as ICacheManager;
            var allowDueToInactivity = cache?.HasRequestScopedFlag("AllowRefreshDueToInactivity") == true;
            
            // Check if we should force logout
            if (tokenStateManager.ShouldForceLogout(tokenState))
            {
                await tokenStateManager.ForceCompleteLogoutAsync();

                // Handle response based on request type
                if (IsApiRequest(context))
                {
                    await WriteUnauthorizedJsonResponse(context, tokenState.LogoutReason);
                    return;
                }
                else
                {
                    // For web requests, force immediate logout and redirect
                    
                    var authScheme = context.User?.Identity?.AuthenticationType;
                    
                    if (authScheme == "AuthenticationTypes.Federation")
                    {
                        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                        // Resolve the correct OIDC sign-out scheme dynamically.
                        // DynamicAuthenticationSchemeProvider returns the active IdP
                        // (DfE Sign-In or Entra SSO) based on configuration.
                        var schemeProvider = context.RequestServices.GetService<IAuthenticationSchemeProvider>();
                        var signOutScheme = await schemeProvider!.GetDefaultSignOutSchemeAsync();
                        if (signOutScheme != null)
                        {
                            await context.SignOutAsync(signOutScheme.Name);
                        }
                    }
                    else
                    {
                        await context.SignOutAsync();
                    }
                    
                    if (authScheme == "TestAuthentication")
                    {
                        context.Session.Remove("TestAuth:Email");
                        context.Session.Remove("TestAuth:Token");
                    }
                    else if (authScheme == "AuthenticationTypes.Federation")
                    {
                        context.Session.Clear();
                    }
                    
                    // Redirect to home page or login page
                    context.Response.Redirect("/", permanent: false);
                    return;
                }
            }
            else if (allowDueToInactivity || (tokenState.CanRefresh && tokenState.IsAnyTokenExpired))
            {
                _logger.LogInformation(">>>>>>>>>> TokenManagement >>> Attempting token refresh for user: {UserName}", userName);
                
                var refreshed = await tokenStateManager.RefreshTokensIfPossibleAsync();
                if (refreshed)
                {
                    _logger.LogInformation(">>>>>>>>>> TokenManagement >>> Token refresh successful for user: {UserName}", userName);
                }
                else
                {
                    _logger.LogWarning(">>>>>>>>>> TokenManagement >>> Token refresh failed for user: {UserName}", userName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, ">>>>>>>>>> TokenManagement >>> Error during token management for user: {UserName}", userName);
            // Continue processing - don't break the request for token management errors
        }

        await _next(context);

        // After successful pipeline execution, record last activity for authenticated users
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            try
            {
                var userId = context.User?.Identity?.Name;
                if (!string.IsNullOrEmpty(userId))
                {
                    var cache = context.RequestServices.GetService(typeof(ICacheManager)) as ICacheManager;
                    if (cache != null)
                    {
                        await cache.SetLastActivityAsync(userId, DateTime.UtcNow, TimeSpan.FromHours(2));
                    }
                }
            }
            catch
            {
                // best-effort
            }
        }
    }

    private static bool IsApiRequest(HttpContext context)
    {
        return context.Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase) ||
               context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
               context.Request.Headers.ContainsKey("X-Requested-With");
    }

    private async Task WriteUnauthorizedJsonResponse(HttpContext context, string? reason)
    {
        context.Response.StatusCode = 401;
        context.Response.ContentType = "application/json";

        var response = new
        {
            error = "unauthorized",
            message = "Authentication tokens have expired",
            reason = reason ?? "Token expiry",
            timestamp = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(response);
        await context.Response.WriteAsync(json);
    }
}