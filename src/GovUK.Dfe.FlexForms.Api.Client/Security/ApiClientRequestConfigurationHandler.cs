using GovUK.Dfe.FlexForms.Api.Client.Settings;

namespace GovUK.Dfe.FlexForms.Api.Client.Security;

/// <summary>
/// Applies per-request API client settings (base URL) before other handlers run.
/// Required for multi-tenant Web hosts where each tenant may target a different API base address.
/// </summary>
public sealed class ApiClientRequestConfigurationHandler(IApiClientSettingsProvider settingsProvider)
    : DelegatingHandler
{
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var settings = settingsProvider.GetSettings();
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            var baseUri = new Uri(settings.BaseUrl.TrimEnd('/') + "/");
            var relativePath = request.RequestUri!.IsAbsoluteUri
                ? request.RequestUri.PathAndQuery
                : request.RequestUri.ToString();
            request.RequestUri = new Uri(baseUri, relativePath);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
