using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GovUK.Dfe.CoreLibs.Security.Configurations;
using GovUK.Dfe.FlexForms.Application.Users;
using Microsoft.IdentityModel.Tokens;

namespace GovUK.Dfe.FlexForms.Application.Tests.Users;

public class TestSubjectTokenDetectorTests
{
    private static readonly TestAuthenticationOptions TestOpts = new()
    {
        Enabled = true,
        JwtSigningKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
        JwtIssuer = "test-external-applications",
        JwtAudience = "test-audience"
    };

    [Fact]
    public void LooksLikeTestToken_ReturnsTrue_ForHmacToken()
    {
        var token = CreateHs256Token(issuer: "anything");

        Assert.True(TestSubjectTokenDetector.LooksLikeTestToken(token, TestOpts));
    }

    [Fact]
    public void LooksLikeTestToken_ReturnsFalse_ForRs256StyleOidcToken()
    {
        // ReadJwtToken only needs a parseable JWT; alg RS256 must not count as test.
        var token =
            "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9." +
            "eyJpc3MiOiJodHRwczovL3Rlc3Qtb2lkYy5zaWduaW4uZWR1Y2F0aW9uLmdvdi51azo0NDMiLCJhdWQiOiJSU0RFeHRlcm5hbEFwcHMifQ." +
            "sig";

        Assert.False(TestSubjectTokenDetector.LooksLikeTestToken(token, TestOpts));
    }

    [Fact]
    public void ForTokenValidation_DisablesTestPath_WhenOidcTokenAndTestEnabled()
    {
        var oidcToken =
            "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9." +
            "eyJpc3MiOiJodHRwczovL3Rlc3Qtb2lkYy5zaWduaW4uZWR1Y2F0aW9uLmdvdi51azo0NDMifQ." +
            "sig";

        var result = TestSubjectTokenDetector.ForTokenValidation(TestOpts, oidcToken);

        Assert.NotNull(result);
        Assert.False(result!.Enabled);
    }

    [Fact]
    public void ForTokenValidation_KeepsEnabled_WhenTokenIsTestJwt()
    {
        var token = CreateHs256Token(issuer: TestOpts.JwtIssuer);

        var result = TestSubjectTokenDetector.ForTokenValidation(TestOpts, token);

        Assert.Same(TestOpts, result);
        Assert.True(result!.Enabled);
    }

    private static string CreateHs256Token(string issuer)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestOpts.JwtSigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Email, "a@b.c") }),
            Issuer = issuer,
            Audience = TestOpts.JwtAudience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = creds
        }));
    }
}
