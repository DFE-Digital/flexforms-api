using System;
using System.Diagnostics.CodeAnalysis;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using GovUK.Dfe.CoreLibs.Http.Models;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Request;
using GovUK.Dfe.FlexForms.Api.Client.Contracts;
using System.Net.Http.Headers;
using GovUK.Dfe.FlexForms.Api.Client.Settings;

namespace GovUK.Dfe.FlexForms.Api.Client.Security;

/// <summary>
/// Automatically registers users when they authenticate with an external IDP for the first time.
/// Intercepts "User not found" errors from token exchange and creates the user account seamlessly.
/// </summary>
[ExcludeFromCodeCoverage]
public class UserAutoRegistrationHandler : DelegatingHandler
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITokenStateManager _tokenStateManager;
    private readonly ITokenAcquisitionService _tokenAcquisitionService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IApiClientSettingsProvider _settingsProvider;
    private readonly ILogger<UserAutoRegistrationHandler> _logger;
    private readonly SemaphoreSlim _registrationLock = new(1, 1);
    public const string TenantIdHeaderName = "X-Tenant-ID";

    public UserAutoRegistrationHandler(
        IHttpClientFactory httpClientFactory,
        ITokenStateManager tokenStateManager,
        ITokenAcquisitionService tokenAcquisitionService,
        IHttpContextAccessor httpContextAccessor,
        IApiClientSettingsProvider settingsProvider,
        ILogger<UserAutoRegistrationHandler> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenStateManager = tokenStateManager;
        _tokenAcquisitionService = tokenAcquisitionService;
        _httpContextAccessor = httpContextAccessor;
        _settingsProvider = settingsProvider;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var settings = _settingsProvider.GetSettings();

        // If auto-registration is disabled, just pass through
        if (!settings.AutoRegisterUsers)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        // First attempt - try the request
        var response = await base.SendAsync(request, cancellationToken);

        // Check if this is a recoverable auth error from token exchange
        // (user not found, or existing user without tenant membership).
        if (await IsAutoRegisterableExchangeErrorAsync(response, request))
        {
            _logger.LogInformation(
                "Token exchange failed due to missing user or tenant membership. Attempting auto-registration...");

            // Use semaphore to prevent duplicate registrations from concurrent requests
            await _registrationLock.WaitAsync(cancellationToken);
            try
            {
                // Try to auto-register the user (creates user and/or TenantMembership)
                var registered = await TryAutoRegisterUserAsync(cancellationToken);

                if (registered)
                {
                    _logger.LogInformation("User auto-registered successfully. Retrying original request...");

                    // Clone and retry the original request now that user exists
                    var retryRequest = await CloneRequestAsync(request);
                    response = await base.SendAsync(retryRequest, cancellationToken);
                }
                else
                {
                    _logger.LogWarning("User auto-registration failed. Returning original error response.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during user auto-registration. Returning original error response.");
            }
            finally
            {
                _registrationLock.Release();
            }
        }

        return response;
    }

    private static async Task<bool> IsAutoRegisterableExchangeErrorAsync(
        HttpResponseMessage response,
        HttpRequestMessage request)
    {
        // Only intercept errors from token exchange endpoint
        if (!request.RequestUri?.AbsolutePath.Contains("/tokens/exchange", StringComparison.OrdinalIgnoreCase) == true)
        {
            return false;
        }

        // Check for 403 Forbidden or 404 Not Found (typical for user not found / not a member)
        if (response.StatusCode != HttpStatusCode.Forbidden &&
            response.StatusCode != HttpStatusCode.InternalServerError &&
            response.StatusCode != HttpStatusCode.NotFound)
        {
            return false;
        }

        // Try to read the error message to confirm it's a recoverable registration error
        try
        {
            var content = await response.Content.ReadAsStringAsync();

            if (string.IsNullOrEmpty(content))
                return false;

            // Brand-new users: "User not found for email ..."
            var isUserNotFound =
                content.Contains("user not found", StringComparison.OrdinalIgnoreCase)
                || (content.Contains("user", StringComparison.OrdinalIgnoreCase)
                    && content.Contains("does not exist", StringComparison.OrdinalIgnoreCase));

            // Existing users without TenantMembership: "User is not a member of tenant '...'"
            var isNotTenantMember =
                content.Contains("not a member of tenant", StringComparison.OrdinalIgnoreCase);

            return isUserNotFound || isNotTenantMember;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TryAutoRegisterUserAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Get the current token state
            var tokenState = await _tokenStateManager.GetCurrentTokenStateAsync();

            // Ensure we have a valid external IDP token
            if (!tokenState.ExternalIdpToken.IsValid || string.IsNullOrEmpty(tokenState.ExternalIdpToken.Value))
            {
                _logger.LogWarning("Cannot auto-register user: External IDP token is missing or invalid");
                return false;
            }

            if (!IsEducationIssuer(tokenState.ExternalIdpToken.Value))
            {
                _logger.LogWarning(
                    "Cannot auto-register user: Token issuer does not contain 'education' or 'login.microsoftonline.com'. Auto-registration is only allowed for education identity providers.");
                return false;
            }

            // Get Azure AD token (service-to-service auth, not OBO)
            var azureToken = await _tokenAcquisitionService.GetTokenAsync();
            if (string.IsNullOrEmpty(azureToken))
            {
                _logger.LogWarning("Cannot auto-register user: Unable to acquire Azure AD token");
                return false;
            }

            // Template access is resolved by the API using SelfRegistrationAccessRules:
            // every live tenant form gets Template R/W + TenantMembership (User).
            _logger.LogInformation("Auto-registering user; template access will be resolved by the API from live tenant forms");

            // Create the registration request.
            // Guid.Empty means "no explicit template" — the API auto-assigns all live tenant forms.
            var registerRequest = new RegisterUserRequest
            {
                AccessToken = tokenState.ExternalIdpToken.Value,
                TemplateId = Guid.Empty
            };

            var settings = _settingsProvider.GetSettings();

            // Call the register endpoint using a local client with Azure token only for this request
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(settings.BaseUrl!);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", azureToken);
            client.DefaultRequestHeaders.Add(TenantIdHeaderName, settings.TenantId.ToString());

            var usersClient = new UsersClient(settings.BaseUrl!, client);
            var result = await usersClient.RegisterUserAsync(registerRequest, cancellationToken);

            _logger.LogInformation("User auto-registered successfully: {UserId} - {Email}", 
                result.UserId, result.Email);

            return true;
        }
        catch (ExternalApplicationsException<ExceptionResponse> ex)
        {
            _logger.LogError(ex, "Auto-registration failed with API error: {StatusCode} - {Message}", 
                ex.StatusCode, ex.Result?.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during user auto-registration");
            return false;
        }
    }

    private bool IsEducationIssuer(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            
            // Check if the token can be read (basic validation)
            if (!handler.CanReadToken(token))
            {
                _logger.LogWarning("Cannot read external IDP token for issuer validation");
                return false;
            }

            var jwtToken = handler.ReadJwtToken(token);
            var issuer = jwtToken.Issuer;

            if (string.IsNullOrEmpty(issuer))
            {
                _logger.LogWarning("External IDP token does not contain an issuer claim");
                return false;
            }

            // Check if issuer contains "education" (case-insensitive)
            var isEducationIssuer = issuer.Contains("education", StringComparison.OrdinalIgnoreCase) || issuer.Contains("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase);

            _logger.LogInformation("Token issuer validation: {Issuer} - IsEducation: {IsEducation}", 
                issuer, isEducationIssuer);

            return isEducationIssuer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating token issuer");
            return false;
        }
    }

    private async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version
        };

        // Copy headers
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // Copy content if present
        if (request.Content != null)
        {
            var contentBytes = await request.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(contentBytes);

            // Copy content headers
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}

