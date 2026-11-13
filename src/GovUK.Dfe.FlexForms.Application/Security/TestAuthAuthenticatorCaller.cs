using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace GovUK.Dfe.FlexForms.Application.Security;

/// <summary>
/// Shared checks for the signed-in user's own authenticator enrolment endpoints. The email always comes from the
/// user's token, never from the request, so nobody can enrol an authenticator for someone else.
/// </summary>
internal static class TestAuthAuthenticatorCaller
{
    public const string NotEnabledMessage = "Test authentication is not enabled for this tenant.";

    /// <summary>The label authenticator apps show for this service, for example <c>Transfers (Test)</c>.</summary>
    public static string BuildIssuer(TenantConfiguration tenant, IHostEnvironment hostEnvironment)
        => $"{Users.Commands.SendTestAuthPasswordCommandHandler.ResolveServiceName(tenant)} ({hostEnvironment.EnvironmentName})";

    /// <summary>
    /// Returns the caller's email, or the reason the caller may not manage an authenticator
    /// (<c>Not authenticated</c> maps to 401, anything else to 403).
    /// </summary>
    public static (string? Email, string? Error) Resolve(
        IHttpContextAccessor httpContextAccessor,
        IHostEnvironment hostEnvironment,
        ITenantContextAccessor tenantContextAccessor)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return (null, "Not authenticated");

        var email = EntraClientIdentity.PickEmail(user)?.Trim();
        if (string.IsNullOrEmpty(email) || !email.Contains('@'))
            return (null, "No user identifier");

        return TestAuthenticationEnvironmentGate.IsEnabledForTenant(hostEnvironment, tenantContextAccessor.CurrentTenant)
            ? (email, null)
            : (null, NotEnabledMessage);
    }
}
