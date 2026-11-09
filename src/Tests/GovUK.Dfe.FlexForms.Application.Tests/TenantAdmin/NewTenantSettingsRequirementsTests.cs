using GovUK.Dfe.FlexForms.Application.TenantAdmin;
using GovUK.Dfe.FlexForms.Domain.Tenancy;

namespace GovUK.Dfe.FlexForms.Application.Tests.TenantAdmin;

public class NewTenantSettingsRequirementsTests
{
    private static readonly string SigningKey = new('k', 32);

    internal static List<NewTenantSetting> DfESignInTenant() =>
    [
        new("ConnectionStrings", "Api", """{"DefaultConnection":"Server=db;Database=ea;"}""", true),
        new("Authorization", "Api", $$$"""{"TokenSettings":{"SecretKey":"{{{SigningKey}}}","Issuer":"iss","Audience":"aud"}}""", true),
        new("AzureAd", "Api", """{"TenantId":"entra-tenant","ClientId":"api-client"}""", true),
        new("Authentication", "Web", """{"Scheme":"DfESignIn"}""", false),
        new("DfESignIn", "Shared", """{"Authority":"https://signin.example.test","DiscoveryEndpoint":"https://signin.example.test/.well-known/openid-configuration","ClientId":"dsi-client","ClientSecret":"dsi-value"}""", true)
    ];

    [Fact]
    public void Validate_ShouldFail_WhenAzureAdHasNoAudienceOrClientId()
    {
        var settings = Replace(DfESignInTenant(), new NewTenantSetting("AzureAd", "Api", """{"TenantId":"entra-tenant"}""", true));

        var errors = NewTenantSettingsRequirements.Validate(settings);

        Assert.Contains(errors, e => e.Contains("Audience or ClientId"));
    }

    [Fact]
    public void Validate_ShouldFail_WhenDfESignInHasNoDiscoveryEndpoint()
    {
        var settings = Replace(DfESignInTenant(), new NewTenantSetting(
            "DfESignIn", "Shared", """{"Authority":"https://signin.example.test","ClientId":"dsi-client","ClientSecret":"dsi-value"}""", true));

        var errors = NewTenantSettingsRequirements.Validate(settings);

        Assert.Contains(errors, e => e.StartsWith("DfESignIn:DiscoveryEndpoint (Api or Shared)"));
    }

    [Fact]
    public void Validate_ShouldFail_WhenApiEntraSsoIsDisabled()
    {
        var settings = EntraTenant().Where(s => s.Category != "EntraSso").ToList();
        settings.Add(new NewTenantSetting("EntraSso", "Web", """{"Enabled":true,"TenantId":"t","ClientId":"c"}""", true));
        settings.Add(new NewTenantSetting("EntraSso", "Api", """{"Enabled":false,"TenantId":"t","ClientId":"c"}""", true));

        var errors = NewTenantSettingsRequirements.Validate(settings);

        Assert.Contains(errors, e => e.StartsWith("EntraSso:Enabled must be true"));
    }

    [Fact]
    public void Validate_ShouldPass_ForMinimalDfESignInTenant()
    {
        Assert.Empty(NewTenantSettingsRequirements.Validate(DfESignInTenant()));
    }

    [Theory]
    [InlineData("ConnectionStrings", "ConnectionStrings:DefaultConnection")]
    [InlineData("Authorization", "Authorization:TokenSettings:SecretKey")]
    [InlineData("AzureAd", "AzureAd")]
    [InlineData("Authentication", "Authentication:Scheme")]
    public void Validate_ShouldFail_WhenAlwaysRequiredCategoryIsMissing(string category, string expected)
    {
        var settings = DfESignInTenant().Where(s => s.Category != category).ToList();

        var errors = NewTenantSettingsRequirements.Validate(settings);

        Assert.Contains(errors, e => e.Contains(expected));
    }

    [Fact]
    public void Validate_ShouldFail_WhenAuthorizationSecretIsTooShort()
    {
        var settings = Replace(DfESignInTenant(), new NewTenantSetting(
            "Authorization", "Api", """{"TokenSettings":{"SecretKey":"short","Issuer":"iss","Audience":"aud"}}""", true));

        var errors = NewTenantSettingsRequirements.Validate(settings);

        Assert.Contains(errors, e => e.Contains("at least 32 characters"));
    }

    [Fact]
    public void Validate_ShouldFail_WhenDfESignInIsOnlyOnWeb_BecauseApiCannotValidateTheToken()
    {
        var settings = Replace(DfESignInTenant(), new NewTenantSetting(
            "DfESignIn", "Web", """{"Authority":"https://signin.example.test","DiscoveryEndpoint":"https://signin.example.test/.well-known/openid-configuration","ClientId":"dsi-client","ClientSecret":"dsi-value"}""", true));

        var errors = NewTenantSettingsRequirements.Validate(settings);

        Assert.Contains(errors, e => e.StartsWith("DfESignIn (Api or Shared)"));
        Assert.Contains(errors, e => e.StartsWith("DfESignIn:DiscoveryEndpoint (Api or Shared)"));
    }

    [Fact]
    public void Validate_ShouldRequireTokenRefresh_ForEntraTenantWithoutDfESignIn()
    {
        var settings = EntraTenant();

        var errors = NewTenantSettingsRequirements.Validate(settings);

        Assert.Contains(errors, e => e.Contains("TokenRefresh"));
    }

    [Fact]
    public void Validate_ShouldPass_ForEntraTenantWithTokenRefresh()
    {
        var settings = EntraTenant();
        settings.Add(new NewTenantSetting(
            "TokenRefresh",
            "Web",
            """{"TokenEndpoint":"https://idp.example.test/token","IntrospectionEndpoint":"https://idp.example.test/introspect","ClientId":"c","ClientSecret":"s"}""",
            true));

        Assert.Empty(NewTenantSettingsRequirements.Validate(settings));
    }

    [Fact]
    public void Validate_ShouldRequireEmailAndPasswordTemplate_ForTestAuthentication()
    {
        var settings = TestAuthTenant(includeEmail: false);

        var errors = NewTenantSettingsRequirements.Validate(settings);

        Assert.Contains(errors, e => e.Contains("Email:Provider"));
        Assert.Contains(errors, e => e.Contains("TestAuthPasswordEmail"));
    }

    [Fact]
    public void Validate_ShouldPass_ForTestAuthenticationWithEmail()
    {
        Assert.Empty(NewTenantSettingsRequirements.Validate(TestAuthTenant(includeEmail: true)));
    }

    [Fact]
    public void Validate_ShouldFail_WhenTestAuthenticationDiffersBetweenApiAndWeb()
    {
        var settings = TestAuthTenant(includeEmail: true)
            .Where(s => s.Category != "TestAuthentication")
            .ToList();
        settings.Add(TestAuth("Api", SigningKey));
        settings.Add(TestAuth("Web", new string('x', 32)));

        var errors = NewTenantSettingsRequirements.Validate(settings);

        Assert.Contains(errors, e => e.Contains("TestAuthentication:JwtSigningKey must be the same"));
    }

    private static List<NewTenantSetting> EntraTenant()
    {
        var settings = DfESignInTenant()
            .Where(s => s.Category is not ("DfESignIn" or "Authentication"))
            .ToList();
        settings.Add(new NewTenantSetting("Authentication", "Web", """{"Scheme":"EntraSso"}""", false));
        settings.Add(new NewTenantSetting("EntraSso", "Shared", """{"Enabled":true,"TenantId":"t","ClientId":"c"}""", true));
        return settings;
    }

    private static List<NewTenantSetting> TestAuthTenant(bool includeEmail)
    {
        var settings = DfESignInTenant()
            .Where(s => s.Category != "Authentication")
            .ToList();
        settings.Add(new NewTenantSetting("Authentication", "Web", """{"Scheme":"TestAuthentication"}""", false));
        settings.Add(TestAuth("Shared", SigningKey));

        if (includeEmail)
        {
            settings.Add(new NewTenantSetting("Email", "Api", """{"Provider":"GovUkNotify","GovUkNotify":{"ApiKey":"notify"}}""", true));
            settings.Add(new NewTenantSetting("EmailTemplates", "Api", """{"Product":{"TestAuthPasswordEmail":"template-id"}}""", false));
        }

        return settings;
    }

    private static NewTenantSetting TestAuth(string target, string signingKey) =>
        new("TestAuthentication", target,
            $$"""{"Enabled":true,"JwtSigningKey":"{{signingKey}}","JwtIssuer":"iss","JwtAudience":"aud"}""", true);

    private static List<NewTenantSetting> Replace(List<NewTenantSetting> settings, NewTenantSetting replacement) =>
        settings.Where(s => s.Category != replacement.Category).Append(replacement).ToList();
}
