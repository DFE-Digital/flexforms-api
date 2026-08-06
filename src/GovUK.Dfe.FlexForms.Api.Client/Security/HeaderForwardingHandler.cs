using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GovUK.Dfe.FlexForms.Api.Client.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Api.Client.Security
{
    /// <summary>
    /// HTTP message handler that forwards specific headers from incoming requests to outgoing API calls.
    /// This is used to forward authentication-related headers (like Cypress test headers) from the web app to the API.
    /// Also automatically appends the X-Tenant-ID header if configured.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class HeaderForwardingHandler : DelegatingHandler
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IApiClientSettingsProvider _settingsProvider;
        private readonly ILogger<HeaderForwardingHandler> _logger;
        private string[]? _headersToForward;

        /// <summary>
        /// The header name used to identify the tenant for multi-tenant API requests.
        /// </summary>
        public const string TenantIdHeaderName = "X-Tenant-ID";

        /// <summary>
        /// Correlation header expected by API <c>CorrelationIdMiddleware</c>.
        /// </summary>
        public const string CorrelationIdHeaderName = "x-correlationId";

        /// <summary>
        /// Default headers that should be forwarded from incoming requests to API calls if not configured
        /// </summary>
        private static readonly string[] DefaultHeadersToForward = new[]
        {
            "x-service-email",
            "x-service-api-key",
            CorrelationIdHeaderName
        };

        /// <summary>
        /// Initializes a new instance of the HeaderForwardingHandler
        /// </summary>
        /// <param name="httpContextAccessor">Accessor to get the current HTTP context</param>
        /// <param name="apiSettings">API client settings containing configuration for headers to forward</param>
        /// <param name="logger">Logger for diagnostic information</param>
        public HeaderForwardingHandler(
            IHttpContextAccessor httpContextAccessor,
            IApiClientSettingsProvider settingsProvider,
            ILogger<HeaderForwardingHandler> logger)
        {
            _httpContextAccessor = httpContextAccessor;
            _settingsProvider = settingsProvider;
            _logger = logger;
            _headersToForward = null;
        }

        private string[] GetHeadersToForward()
        {
            if (_headersToForward is not null)
            {
                return _headersToForward;
            }

            var apiSettings = _settingsProvider.GetSettings();
            _headersToForward = apiSettings.HeadersToForward?.Any() == true
                ? apiSettings.HeadersToForward
                : DefaultHeadersToForward;

            return _headersToForward;
        }

        /// <summary>
        /// Sends an HTTP request, forwarding configured headers from the incoming request
        /// and appending the X-Tenant-ID header if configured.
        /// </summary>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var tenantId = _settingsProvider.GetSettings().TenantId;
            if (tenantId.HasValue)
            {
                if (request.Headers.Contains(TenantIdHeaderName))
                {
                    request.Headers.Remove(TenantIdHeaderName);
                }

                request.Headers.Add(TenantIdHeaderName, tenantId.Value.ToString());

                _logger.LogDebug(
                    "Added {HeaderName} header with value {TenantId} to API request: {RequestUri}",
                    TenantIdHeaderName,
                    tenantId.Value,
                    request.RequestUri);
            }

            var httpContext = _httpContextAccessor.HttpContext;

            if (httpContext != null)
            {
                EnsureCorrelationIdHeader(httpContext);

                var headersForwarded = 0;

                // Always forward correlation id (even if HeadersToForward omits it).
                if (httpContext.Request.Headers.TryGetValue(CorrelationIdHeaderName, out var correlationValue)
                    && !string.IsNullOrEmpty(correlationValue.ToString()))
                {
                    if (request.Headers.Contains(CorrelationIdHeaderName))
                        request.Headers.Remove(CorrelationIdHeaderName);

                    request.Headers.Add(CorrelationIdHeaderName, correlationValue.ToString());
                    headersForwarded++;
                }

                // Forward each configured header if present in the incoming request
                foreach (var headerName in GetHeadersToForward())
                {
                    if (string.Equals(headerName, CorrelationIdHeaderName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (httpContext.Request.Headers.TryGetValue(headerName, out var headerValue))
                    {
                        var value = headerValue.ToString();
                        if (!string.IsNullOrEmpty(value))
                        {
                            if (request.Headers.Contains(headerName))
                            {
                                request.Headers.Remove(headerName);
                            }

                            request.Headers.Add(headerName, value);
                            headersForwarded++;

                            _logger.LogDebug(
                                "Forwarded header {HeaderName} to API request: {RequestUri}",
                                headerName,
                                request.RequestUri);
                        }
                    }
                }

                if (headersForwarded > 0)
                {
                    _logger.LogDebug(
                        "Forwarded {Count} header(s) to API request: {RequestUri}",
                        headersForwarded,
                        request.RequestUri);
                }
            }
            else if (!request.Headers.Contains(CorrelationIdHeaderName))
            {
                // Background / no HttpContext: still send a correlation id for API tracing.
                request.Headers.Add(CorrelationIdHeaderName, Guid.NewGuid().ToString());
            }

            return await base.SendAsync(request, cancellationToken);
        }

        private static void EnsureCorrelationIdHeader(HttpContext httpContext)
        {
            if (httpContext.Request.Headers.TryGetValue(CorrelationIdHeaderName, out var existing)
                && Guid.TryParse(existing.ToString(), out _))
            {
                return;
            }

            var correlationId = Guid.NewGuid().ToString();
            httpContext.Request.Headers[CorrelationIdHeaderName] = correlationId;
            httpContext.Response.Headers[CorrelationIdHeaderName] = correlationId;
        }
    }
}

