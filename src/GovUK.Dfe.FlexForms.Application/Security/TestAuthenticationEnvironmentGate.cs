using GovUK.Dfe.FlexForms.Application.TenantAdmin;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Hosting;

namespace GovUK.Dfe.FlexForms.Application.Security;

/// <summary>
/// Hard rule: Test Authentication may only run in explicitly allowed environments,
/// regardless of tenant <c>Authentication:Scheme</c> or <c>TestAuthentication:Enabled</c>.
/// </summary>
public static class TestAuthenticationEnvironmentGate
{
    private static readonly HashSet<string> AllowedEnvironments = new(StringComparer.OrdinalIgnoreCase)
    {
        Environments.Development,
        "Local",
        "Test"
    };

    /// <summary>
    /// Returns <c>true</c> when Test Authentication may be used in this environment.
    /// Missing environment information fails closed.
    /// </summary>
    public static bool IsAllowed(IHostEnvironment? environment)
        => environment is not null && IsAllowed(environment.EnvironmentName);

    /// <summary>
    /// Returns <c>true</c> when Test Authentication may be used for the given environment name.
    /// Allow-list: Development, Local, Test.
    /// </summary>
    public static bool IsAllowed(string? environmentName)
    {
        if (string.IsNullOrWhiteSpace(environmentName))
            return false;

        return AllowedEnvironments.Contains(environmentName.Trim());
    }

    /// <summary>
    /// Returns <c>true</c> when the host environment is Production (or Prod).
    /// </summary>
    public static bool IsProduction(IHostEnvironment? environment)
    {
        if (environment is null || string.IsNullOrWhiteSpace(environment.EnvironmentName))
            return false;

        return string.Equals(environment.EnvironmentName, Environments.Production, StringComparison.OrdinalIgnoreCase)
               || string.Equals(environment.EnvironmentName, "Prod", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns <c>true</c> when the environment allows Test Authentication and the tenant has
    /// <c>TestAuthentication:Enabled</c> set to <c>true</c>.
    /// </summary>
    public static bool IsEnabledForTenant(IHostEnvironment? environment, TenantConfiguration? tenant)
        => tenant is not null
           && IsAllowed(environment)
           && TenantInteractiveAuthSchemeResolver.GetTestAuthenticationEnabled(tenant.Settings);
}
