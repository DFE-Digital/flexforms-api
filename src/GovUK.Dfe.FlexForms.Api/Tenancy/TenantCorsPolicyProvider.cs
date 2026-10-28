using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace GovUK.Dfe.FlexForms.Api.Tenancy;

public class TenantCorsPolicyProvider : ICorsPolicyProvider
{
    private readonly DefaultCorsPolicyProvider _defaultProvider;
    private readonly IWebHostEnvironment _environment;

    public TenantCorsPolicyProvider(IOptions<CorsOptions> options, IWebHostEnvironment environment)
    {
        _defaultProvider = new DefaultCorsPolicyProvider(options);
        _environment = environment;
    }

    public Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
    {
        if (!string.Equals(policyName, "Frontend", StringComparison.OrdinalIgnoreCase))
            return _defaultProvider.GetPolicyAsync(context, policyName);

        var tenantContextAccessor = context.RequestServices.GetService<ITenantContextAccessor>();
        var tenant = tenantContextAccessor?.CurrentTenant;

        if (tenant is null)
        {
            var origin = context.Request.Headers.Origin.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(origin))
            {
                var tenants = context.RequestServices.GetService<ITenantConfigurationProvider>();
                tenant = tenants?.GetTenantByOrigin(origin);
            }
        }

        if (tenant?.FrontendOrigins is { Length: > 0 })
            return Task.FromResult<CorsPolicy?>(Build(tenant.FrontendOrigins));

        if (_environment.IsDevelopment())
            return Task.FromResult<CorsPolicy?>(Build(["https://localhost:7020"]));

        return Task.FromResult<CorsPolicy?>(null);
    }

    private static CorsPolicy Build(string[] origins) =>
        new CorsPolicyBuilder()
            .WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .Build();
}
