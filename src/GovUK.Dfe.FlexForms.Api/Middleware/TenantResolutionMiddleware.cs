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
        "/_",
        "/favicon.ico",
        "/v1/tenant-config",
        "/v1/host-config"
    };

    private static bool IsPlatformTenantConfigPath(string path) =>
        path.StartsWith("/v1/tenant-config/tenants/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/v1/tenant-config/resolve", StringComparison.OrdinalIgnoreCase);

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
        var conf = _tenantConfigurationProvider.GetAllTenants();
        // Bypass for infrastructure endpoints and CORS preflight
        if (context.Request.Method == "OPTIONS" ||
            BypassPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)) ||
            IsPlatformTenantConfigPath(path))
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
            _logger.LogWarning(ex, "Tenant resolution failed");
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
                throw new InvalidTenantException($"Tenant '{tenantIdFromHeader}' is not configured.");
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

        throw new InvalidTenantException("Missing or invalid tenant id header.");
    }

    private static async Task RespondInvalidTenant(HttpContext context, string message)
    {
        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
        context.Response.ContentType = "application/json";
        var response = JsonSerializer.Serialize(new { error = message });
        await context.Response.WriteAsync(response);
    }

    private class InvalidTenantException : Exception
    {
        public InvalidTenantException(string message) : base(message)
        {
        }
    }
}
