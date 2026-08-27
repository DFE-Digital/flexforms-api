using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;

namespace GovUK.Dfe.FlexForms.Application.Services;

/// <summary>
/// Ensures the current tenant has Email settings required for outbound mail.
/// Host <c>GlobalConfiguration:Email</c> registers <c>IEmailService</c> at startup only;
/// missing or deleted tenant Email must fail the tenant, not silently use host values.
/// </summary>
public static class TenantEmailConfiguration
{
    public static void EnsureConfigured(ITenantContextAccessor tenantContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(tenantContextAccessor);

        var tenant = tenantContextAccessor.CurrentTenant
            ?? throw new InvalidOperationException(
                "No tenant context available for email operation.");

        var provider = tenant.Settings.GetValue<string>("Email:Provider");
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new InvalidOperationException(
                $"Tenant '{tenant.Name}' has no Email:Provider configured. " +
                "Email operations require a tenant Email setting and do not fall back to host GlobalConfiguration.");
        }

        if (string.Equals(provider, "GovUkNotify", StringComparison.OrdinalIgnoreCase))
        {
            var apiKey = tenant.Settings.GetValue<string>("Email:GovUkNotify:ApiKey");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException(
                    $"Tenant '{tenant.Name}' Email Provider is GovUkNotify but Email:GovUkNotify:ApiKey is missing.");
            }
        }
    }
}
