using System.IdentityModel.Tokens.Jwt;
using GovUK.Dfe.CoreLibs.Security.Configurations;

namespace GovUK.Dfe.FlexForms.Application.Users;

/// <summary>
/// Distinguishes Test Authentication JWTs from DSI/Entra OIDC ID tokens when both
/// may be enabled on the same tenant (interactive scheme vs Cypress/test path).
/// </summary>
internal static class TestSubjectTokenDetector
{
    /// <summary>
    /// Returns options to pass into <c>IExternalIdentityValidator.ValidateIdTokenAsync</c>.
    /// When Test Auth is enabled but the subject token is clearly an OIDC JWT, returns a
    /// disabled copy so CoreLibs does not force the HMAC test-validation path
    /// (and so host <c>TestAuthentication:Enabled</c> cannot win via null fallback).
    /// </summary>
    public static TestAuthenticationOptions? ForTokenValidation(
        TestAuthenticationOptions? tenantOptions,
        string subjectToken)
    {
        if (tenantOptions is null)
        {
            return null;
        }

        if (tenantOptions.Enabled && !LooksLikeTestToken(subjectToken, tenantOptions))
        {
            return new TestAuthenticationOptions { Enabled = false };
        }

        return tenantOptions;
    }

    public static bool IsActiveTestSubjectToken(
        TestAuthenticationOptions? tenantOptions,
        string subjectToken)
        => tenantOptions?.Enabled == true && LooksLikeTestToken(subjectToken, tenantOptions);

    public static bool LooksLikeTestToken(string idToken, TestAuthenticationOptions? options)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return false;
        }

        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(idToken))
        {
            return false;
        }

        try
        {
            var jwt = handler.ReadJwtToken(idToken);
            var alg = jwt.Header.Alg;
            if (!string.IsNullOrEmpty(alg)
                && alg.StartsWith("HS", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (options is not null
                && !string.IsNullOrWhiteSpace(options.JwtIssuer)
                && string.Equals(jwt.Issuer, options.JwtIssuer, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
