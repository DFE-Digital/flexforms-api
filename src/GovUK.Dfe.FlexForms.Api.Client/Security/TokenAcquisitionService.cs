using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using GovUK.Dfe.FlexForms.Api.Client.Settings;

namespace GovUK.Dfe.FlexForms.Api.Client.Security
{
    [ExcludeFromCodeCoverage]
    public class TokenAcquisitionService(
        IApiClientSettingsProvider settingsProvider,
        ILogger<TokenAcquisitionService> logger) : ITokenAcquisitionService
    {
        // Static because this service is scoped; the credential holds the token cache and must outlive the scope.
        private static readonly ConcurrentDictionary<string, ManagedIdentityCredential> ManagedIdentityCredentials = new();

        private readonly ConcurrentDictionary<string, Lazy<IConfidentialClientApplication>> _applications = new();

        public async Task<string> GetTokenAsync()
        {
            var settings = settingsProvider.GetSettings()
                ?? throw new InvalidOperationException("API client settings are not available.");

            if (string.IsNullOrWhiteSpace(settings.Scope))
            {
                throw new InvalidOperationException("ExternalApplicationsApiClient:Scope is not configured.");
            }

            var accessToken = settings.UseManagedIdentity
                ? await AcquireManagedIdentityTokenAsync(settings)
                : await AcquireClientCredentialsTokenAsync(settings);

            if (string.IsNullOrEmpty(accessToken))
            {
                throw new InvalidOperationException("Token acquisition returned empty access token");
            }

            return accessToken;
        }

        private async Task<string?> AcquireManagedIdentityTokenAsync(ApiClientSettings settings)
        {
            var identityClientId = string.IsNullOrWhiteSpace(settings.ManagedIdentityClientId)
                ? null
                : settings.ManagedIdentityClientId;

            var credential = ManagedIdentityCredentials.GetOrAdd(identityClientId ?? string.Empty, _ =>
                new ManagedIdentityCredential(identityClientId is null
                    ? ManagedIdentityId.SystemAssigned
                    : ManagedIdentityId.FromUserAssignedClientId(identityClientId)));

            var token = await credential.GetTokenAsync(new TokenRequestContext(new[] { settings.Scope! }));

            logger.LogDebug("Acquired managed identity token for API client ({Identity})",
                identityClientId ?? "system-assigned");
            return token.Token;
        }

        private async Task<string?> AcquireClientCredentialsTokenAsync(ApiClientSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.ClientId))
            {
                throw new InvalidOperationException("ExternalApplicationsApiClient:ClientId is not configured.");
            }

            if (string.IsNullOrWhiteSpace(settings.ClientSecret))
            {
                throw new InvalidOperationException("ExternalApplicationsApiClient:ClientSecret is not configured.");
            }

            if (string.IsNullOrWhiteSpace(settings.Authority))
            {
                throw new InvalidOperationException("ExternalApplicationsApiClient:Authority is not configured.");
            }

            var cacheKey = $"{settings.Authority}|{settings.ClientId}";
            var application = _applications.GetOrAdd(cacheKey, _ => new Lazy<IConfidentialClientApplication>(() =>
                ConfidentialClientApplicationBuilder.Create(settings.ClientId)
                    .WithClientSecret(settings.ClientSecret)
                    .WithAuthority(new Uri(settings.Authority))
                    .Build())).Value;

            var authResult = await application.AcquireTokenForClient(new[] { settings.Scope })
                .ExecuteAsync();

            logger.LogDebug("Acquired client-credentials token for API client {ClientId}", settings.ClientId);
            return authResult?.AccessToken;
        }
    }
}
