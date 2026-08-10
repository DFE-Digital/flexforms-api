using Microsoft.AspNetCore.Http;

namespace GovUK.Dfe.FlexForms.Api.Security;

/// <summary>
/// Ensures expensive claim enrichment runs at most once per HTTP request when multiple
/// authentication schemes succeed (e.g. TenantBearer then PlatformBearer).
/// </summary>
internal static class RequestClaimEnrichmentGate
{
    public const string AzurePermissionsKey = "FlexForms.AzurePermissionClaimsLoaded";

    /// <summary>
    /// Returns <c>true</c> if enrichment should run (first call for this key on the request).
    /// When <paramref name="httpContext"/> is null (unit tests), always allows enrichment.
    /// </summary>
    public static bool TryBegin(HttpContext? httpContext, string key)
    {
        if (httpContext is null)
            return true;

        if (httpContext.Items.ContainsKey(key))
            return false;

        httpContext.Items[key] = true;
        return true;
    }
}
