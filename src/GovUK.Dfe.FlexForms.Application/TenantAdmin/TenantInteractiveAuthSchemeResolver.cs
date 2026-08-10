using Microsoft.Extensions.Configuration;

namespace GovUK.Dfe.FlexForms.Application.TenantAdmin;

/// <summary>
/// Resolves the interactive authentication scheme from tenant configuration,
/// matching the Web application's <c>TenantAuthSchemeSelector</c> precedence.
/// </summary>
public static class TenantInteractiveAuthSchemeResolver
{
    public static string ResolveSchemeName(IConfiguration settings)
    {
        var explicitRaw = FirstNonEmpty(
            settings["Authentication:Scheme"],
            settings["InteractiveAuthentication:Scheme"],
            settings.GetSection("Authentication")["Scheme"],
            settings.GetSection("InteractiveAuthentication")["Scheme"]);

        if (TryParseSchemeName(explicitRaw, out var explicitScheme))
        {
            return explicitScheme;
        }

        if (GetBool(settings, "TestAuthentication:Enabled"))
        {
            return "TestAuthentication";
        }

        if (GetBool(settings, "EntraSso:Enabled"))
        {
            return "EntraSso";
        }

        return "DfESignIn";
    }

    public static bool GetTestAuthenticationEnabled(IConfiguration settings)
        => GetBool(settings, "TestAuthentication:Enabled");

    public static bool GetEntraSsoEnabled(IConfiguration settings)
        => GetBool(settings, "EntraSso:Enabled");

    public static bool IsDfESignInConfigured(IConfiguration settings)
        => settings.GetSection("DfESignIn").GetChildren().Any();

    private static bool TryParseSchemeName(string? raw, out string schemeName)
    {
        schemeName = "DfESignIn";
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        switch (raw.Trim().ToLowerInvariant())
        {
            case "test":
            case "testauth":
            case "testauthentication":
                schemeName = "TestAuthentication";
                return true;

            case "entra":
            case "entrasso":
            case "entra-sso":
            case "microsoft":
                schemeName = "EntraSso";
                return true;

            case "dsi":
            case "dfesignin":
            case "dfe-signin":
            case "dfesign-in":
            case "openidconnect":
            case "oidc":
                schemeName = "DfESignIn";
                return true;

            default:
                schemeName = raw.Trim();
                return true;
        }
    }

    private static bool GetBool(IConfiguration settings, string key)
        => bool.TryParse(settings[key], out var value) && value;

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
