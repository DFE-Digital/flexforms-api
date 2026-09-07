using System.Security.Claims;

namespace GovUK.Dfe.FlexForms.Domain.Tenancy;

/// <summary>
/// Entra access-token identity helpers. Client-credential (app-only) tokens identify the
/// calling app via <c>azp</c> / <c>appid</c>; FlexForms maps that value to
/// <c>User.ExternalProviderId</c>.
/// </summary>
public static class EntraClientIdentity
{
    public const string AppIdClaimType = "appid";
    public const string AzpClaimType = "azp";
    public const string AppIdSchemaClaimType = "http://schemas.microsoft.com/identity/claims/appid";
    public const string IdTypClaimType = "idtyp";
    public const string IdTypApp = "app";
    public const string IdTypUser = "user";

    /// <summary>
    /// Client id of the app that acquired the token (<c>azp</c> on v2, <c>appid</c> on v1).
    /// </summary>
    public static string? PickClientId(ClaimsPrincipal? principal)
    {
        if (principal is null)
        {
            return null;
        }

        return FirstNonEmpty(
            principal.FindFirst(AzpClaimType)?.Value,
            principal.FindFirst(AppIdClaimType)?.Value,
            principal.FindFirst(AppIdSchemaClaimType)?.Value);
    }

    public static string? PickEmail(ClaimsPrincipal? principal)
    {
        if (principal is null)
        {
            return null;
        }

        return FirstNonEmpty(
            principal.FindFirst(ClaimTypes.Email)?.Value,
            principal.FindFirst(TenantAuthClaimTypes.Email)?.Value,
            principal.FindFirst(TenantAuthClaimTypes.PreferredUsername)?.Value,
            principal.FindFirst("upn")?.Value,
            principal.FindFirst(ClaimTypes.Upn)?.Value);
    }

    /// <summary>
    /// True for Entra issuers used by both v1 (<c>sts.windows.net</c>) and v2
    /// (<c>login.microsoftonline.com</c>) access tokens.
    /// </summary>
    public static bool IsEntraIssuer(string? issuer)
    {
        if (string.IsNullOrEmpty(issuer))
        {
            return false;
        }

        return issuer.Contains("windows.net", StringComparison.OrdinalIgnoreCase)
            || issuer.Contains("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when the token is an app-only / client-credentials credential rather than an
    /// interactive user. Prefer <c>idtyp=app</c>; otherwise require a client id and no email.
    /// </summary>
    public static bool IsAppOnlyToken(ClaimsPrincipal principal)
    {
        var idtyp = principal.FindFirst(IdTypClaimType)?.Value;
        if (string.Equals(idtyp, IdTypApp, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(idtyp, IdTypUser, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var email = PickEmail(principal);
        if (!string.IsNullOrEmpty(email) && email.Contains('@'))
        {
            return false;
        }

        return !string.IsNullOrEmpty(PickClientId(principal));
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
