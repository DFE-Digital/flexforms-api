using System.Net;
using System.Text.Json;
using GovUK.Dfe.FlexForms.Domain.Tenancy;

namespace GovUK.Dfe.FlexForms.Api.Middleware;

public class TenantResolutionMiddleware
{
    public const string TenantIdHeader = "X-Tenant-ID";

    private static readonly string[] BypassPaths =
    {
        "/swagger",
        "/health",
        "/healthz",
        "/liveness",
        "/readiness",
        "/robots.txt",
        "/favicon.ico",
        "/_",
        "/v1/tenant-config",
        "/v1/host-config"
    };

    private static bool IsPlatformTenantConfigPath(string path) =>
        path.StartsWith("/v1/tenant-config/tenants/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/v1/tenant-config/resolve", StringComparison.OrdinalIgnoreCase);

    private static bool IsTenantResolutionBypassPath(string path) =>
        BypassPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))
        || IsPlatformTenantConfigPath(path)
        || path.Equals("/v1/diagnostics", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Azure App Service Always On / front-door style probes often hit the site root with no tenant header.
    /// </summary>
    private static bool IsRootProbe(HttpContext context, string path) =>
        (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        && (string.IsNullOrEmpty(path) || path == "/");

    private readonly RequestDelegate _next;
    private readonly ITenantConfigurationProvider _tenantConfigurationProvider;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    public TenantResolutionMiddleware(
        RequestDelegate next,
        ITenantConfigurationProvider tenantConfigurationProvider,
        ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next;
        _tenantConfigurationProvider = tenantConfigurationProvider;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Bypass for infrastructure endpoints, root probes, and CORS preflight
        if (context.Request.Method == "OPTIONS" ||
            IsRootProbe(context, path) ||
            IsTenantResolutionBypassPath(path))
        {
            await _next(context);
            return;
        }

        // Resolve the scoped tenant context accessor from request services
        var tenantContextAccessor = context.RequestServices.GetRequiredService<ITenantContextAccessor>();

        try
        {
            var (tenantConfig, tenantId) = ResolveTenant(context);

            tenantContextAccessor.CurrentTenant = tenantConfig;
            using (_logger.BeginScope(new Dictionary<string, object>
                   {
                       ["TenantId"] = tenantId,
                       ["TenantName"] = tenantConfig.Name
                   }))
            {
                await _next(context);
            }
        }
        catch (InvalidTenantException ex)
        {
            // Do not log the exception object — these are expected client/probe failures and
            // LogWarning(ex, ...) floods App Insights / container logs with stack traces.
            if (ex.IsMissingTenantContext)
            {
                _logger.LogDebug(
                    "Tenant resolution skipped: no {Header} or matching Origin on {Method} {Path}",
                    TenantIdHeader,
                    context.Request.Method,
                    path);
            }
            else
            {
                _logger.LogWarning(
                    "Tenant resolution failed for {Method} {Path}: {Reason}",
                    context.Request.Method,
                    path,
                    ex.Message);
            }

            await RespondInvalidTenant(context, ex.Message);
        }
    }

    private (TenantConfiguration tenant, Guid tenantId) ResolveTenant(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(TenantIdHeader, out var tenantHeader) &&
            Guid.TryParse(tenantHeader, out var tenantIdFromHeader))
        {
            var tenantFromHeader = _tenantConfigurationProvider.GetTenant(tenantIdFromHeader);
            if (tenantFromHeader is null)
            {
                throw new InvalidTenantException(
                    $"Tenant '{tenantIdFromHeader}' is not configured.",
                    isMissingTenantContext: false);
            }

            return (tenantFromHeader, tenantIdFromHeader);
        }

        if (context.Request.Headers.TryGetValue("Origin", out var originHeader))
        {
            var origin = originHeader.ToString();
            var matchingTenant = _tenantConfigurationProvider.GetTenantByOrigin(origin);

            if (matchingTenant is not null)
            {
                return (matchingTenant, matchingTenant.Id);
            }
        }

        throw new InvalidTenantException(
            "Missing or invalid tenant id header.",
            isMissingTenantContext: true);
    }

    private static async Task RespondInvalidTenant(HttpContext context, string message)
    {
        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
        context.Response.ContentType = "application/json";
        var response = JsonSerializer.Serialize(new { error = message });
        await context.Response.WriteAsync(response);
    }

    private sealed class InvalidTenantException(string message, bool isMissingTenantContext) : Exception(message)
    {
        public bool IsMissingTenantContext { get; } = isMissingTenantContext;
    }
}
