using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.FlexForms.Api.Client.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GovUK.Dfe.FlexForms.Api.Client.Security;

[ExcludeFromCodeCoverage]
public class TokenExchangeHandler(
    IHttpContextAccessor httpContextAccessor,
    IInternalUserTokenStore tokenStore,
    ITokensClient tokensClient,
    ITokenAcquisitionService tokenAcquisitionService,
    ITokenStateManager tokenStateManager,
    ILogger<TokenExchangeHandler> logger)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Check if we should use service-to-service authentication (for background consumers)
        if (AuthenticationContext.UseServiceToServiceAuth)
        {
            logger.LogDebug("Using service-to-service authentication for background operation");
            
            try
            {
                // Use Azure service-to-service token instead of OBO token
                var serviceToken = await tokenAcquisitionService.GetTokenAsync();
                
                if (!string.IsNullOrEmpty(serviceToken))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", serviceToken);
                    return await base.SendAsync(request, cancellationToken);
                }
                else
                {
                    logger.LogError("Failed to acquire service-to-service token for background operation");
                    return UnauthorizedResponse(request);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error acquiring service-to-service token for background operation");
                return UnauthorizedResponse(request);
            }
        }

        // Normal OBO token flow for web requests
        var httpContext = httpContextAccessor.HttpContext;
        
        if (httpContext == null)
        {
            return UnauthorizedResponse(request);
        }

        // Check if user is authenticated
        if (httpContext.User?.Identity?.IsAuthenticated != true)
        {
            return UnauthorizedResponse(request);
        }

        var userName = httpContext.User.Identity.Name ?? "Unknown";

        try
        {
            // Get current token state
            var tokenState = await tokenStateManager.GetCurrentTokenStateAsync();

            // Check if we should force logout
            if (tokenStateManager.ShouldForceLogout(tokenState))
            {
                await tokenStateManager.ForceCompleteLogoutAsync();
                return UnauthorizedResponse(request);
            }

            // Check if we have a valid OBO token
            if (tokenState.OboToken.IsValid)
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenState.OboToken.Value);
                return await base.SendAsync(request, cancellationToken);
            }

            // Attempt token refresh if possible
            if (tokenState.CanRefresh)
            {
                var refreshed = await tokenStateManager.RefreshTokensIfPossibleAsync();
                if (refreshed)
                {
                    tokenState = await tokenStateManager.GetCurrentTokenStateAsync();
                }
            }

            // Try to exchange for OBO token
            if (tokenState.ExternalIdpToken.IsValid && !string.IsNullOrEmpty(tokenState.ExternalIdpToken.Value))
            {
                var exchange = await ExchangeTokenAsync(tokenState.ExternalIdpToken.Value);
                if (!string.IsNullOrEmpty(exchange.AccessToken))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", exchange.AccessToken);
                    return await base.SendAsync(request, cancellationToken);
                }
                else if (exchange.DeniedTenantAccess)
                {
                    // Authorisation, not authentication: IdP/Test Auth succeeded but the EA user is
                    // missing or has no membership in this tenant. Do not set the forced-logout flag
                    // (that signs the user out and sends them to "/", which silently signs them in
                    // again and loops). Return a 403 that Web recognises by ErrorId.
                    logger.LogWarning(
                        "Token exchange denied for user {UserEmail}: not authorised for the current tenant",
                        userName);
                    tokenStore.ClearToken();
                    return NoTenantMembershipResponse(request);
                }
                else
                {
                    await tokenStateManager.ForceCompleteLogoutAsync();
                    return UnauthorizedResponse(request);
                }
            }
            else
            {
                await tokenStateManager.ForceCompleteLogoutAsync();
                return UnauthorizedResponse(request);
            }
        }
        catch (Exception ex) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            // Client navigated away. Returning 401 here would be treated as an auth failure upstream.
            logger.LogWarning(
                "API call {Method} {Path} cancelled because the client aborted the request ({ExceptionType})",
                request.Method,
                request.RequestUri?.AbsolutePath,
                ex.GetType().Name);
            throw new OperationCanceledException("The client aborted the request.", ex, httpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Token exchange handler failed for user {UserEmail}", userName);
            return UnauthorizedResponse(request);
        }
    }

    private readonly record struct ExchangeOutcome(string? AccessToken, bool DeniedTenantAccess)
    {
        public static ExchangeOutcome Failed => new(null, false);
        public static ExchangeOutcome Denied => new(null, true);
    }

    private async Task<ExchangeOutcome> ExchangeTokenAsync(string externalIdpToken)
    {
        try
        {
            // Get Azure token for authentication with the token exchange endpoint
            var azureToken = await tokenAcquisitionService.GetTokenAsync();
            if (string.IsNullOrEmpty(azureToken))
            {
                return ExchangeOutcome.Failed;
            }

            // Create exchange request
            var exchangeRequest = new ExchangeTokenRequest(externalIdpToken);

            // Call exchange endpoint
            var result = await tokensClient.ExchangeAsync(exchangeRequest);

            if (!string.IsNullOrEmpty(result?.AccessToken))
            {
                // Store the new OBO token
                tokenStore.SetToken(result.AccessToken);

                return new ExchangeOutcome(result.AccessToken, false);
            }
            else
            {
                return ExchangeOutcome.Failed;
            }
        }
        catch (ExternalApplicationsException ex) when (TokenExchangeErrors.IsDeniedTenantAccess(ex))
        {
            // Expected when Test Auth / IdP succeeds but the EA user is unknown or has no
            // membership in this tenant (404 user not found, or 403 not a member).
            return ExchangeOutcome.Denied;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to exchange external IDP token for OBO token");
            return ExchangeOutcome.Failed;
        }
    }

    private static HttpResponseMessage NoTenantMembershipResponse(HttpRequestMessage request)
    {
        var errorResponse = new ExceptionResponse
        {
            StatusCode = (int)HttpStatusCode.Forbidden,
            ExceptionType = "Forbidden",
            Message = TokenExchangeErrors.NoTenantMembershipMessage,
            Timestamp = DateTime.UtcNow,
            ErrorId = TokenExchangeErrors.NoTenantMembershipErrorId
        };

        return new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            RequestMessage = request,
            Content = new StringContent(JsonSerializer.Serialize(errorResponse), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage UnauthorizedResponse(HttpRequestMessage request)
    {
        var errorResponse = new ExceptionResponse
        {
            StatusCode = (int)HttpStatusCode.Unauthorized,
            ExceptionType = "Unauthorized",
            Message = "Authentication tokens are invalid or expired",
            Timestamp = DateTime.UtcNow,
            ErrorId = "Exchange-401"
        };

        return new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            RequestMessage = request,
            Content = new StringContent(JsonSerializer.Serialize(errorResponse), Encoding.UTF8, "application/json")
        };
    }
}