using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace GovUK.Dfe.FlexForms.Infrastructure.Prism;

/// <summary>Bearer tokens for Prism's control endpoints, carrying the API identity's Prism app roles.</summary>
public interface IPrismAccessTokenSource
{
    Task<string> GetTokenAsync(string scope, CancellationToken cancellationToken);
}

/// <summary>Uses the managed identity in Azure, or the developer's Azure sign-in locally. Tokens are cached by Azure.Identity.</summary>
public sealed class AzurePrismAccessTokenSource(IOptions<PrismControlApiOptions> options) : IPrismAccessTokenSource
{
    private readonly Lazy<TokenCredential> credential = new(() => new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        ManagedIdentityClientId = options.Value.ManagedIdentityClientId,
    }));

    public async Task<string> GetTokenAsync(string scope, CancellationToken cancellationToken) =>
        (await credential.Value.GetTokenAsync(new TokenRequestContext([scope]), cancellationToken)).Token;
}
