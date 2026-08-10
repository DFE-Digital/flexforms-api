using System.Text.Json.Nodes;
using GovUK.Dfe.FlexForms.Infrastructure.Services;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class TenantDuplicatorSecretRewriteTests
{
    [Fact]
    public void ApplyAuthorizationApiSecretKey_ReplacesTokenSettingsSecretKey()
    {
        var json = """
            {"TokenSettings":{"SecretKey":"old-secret-key-value-32chars!!","Issuer":"iss","Audience":"aud","TokenLifetimeMinutes":60}}
            """;

        var updated = TenantDuplicatorService.ApplyAuthorizationApiSecretKey(
            json,
            "new-authorization-secret-key-32ch",
            Guid.Parse("33333333-3333-4333-8333-333333333333"));

        var root = JsonNode.Parse(updated)!.AsObject();
        Assert.Equal("new-authorization-secret-key-32ch", root["TokenSettings"]!["SecretKey"]!.GetValue<string>());
        Assert.Equal("iss", root["TokenSettings"]!["Issuer"]!.GetValue<string>());
    }

    [Fact]
    public void ApplyAuthorizationApiSecretKey_ReplacesFlatSecretKey_WhenTokenSettingsMissing()
    {
        var json = """
            {"SecretKey":"old-secret-key-value-32chars!!","Issuer":"iss","Audience":"aud"}
            """;

        var updated = TenantDuplicatorService.ApplyAuthorizationApiSecretKey(
            json,
            "new-authorization-secret-key-32ch",
            Guid.Parse("33333333-3333-4333-8333-333333333333"));

        var root = JsonNode.Parse(updated)!.AsObject();
        Assert.Equal("new-authorization-secret-key-32ch", root["SecretKey"]!.GetValue<string>());
        Assert.Equal("iss", root["Issuer"]!.GetValue<string>());
        Assert.Equal("aud", root["Audience"]!.GetValue<string>());
    }

    [Fact]
    public void ApplyAuthorizationApiSecretKey_CreatesTokenSettings_WhenAuthorizationPayloadHasNoSecrets()
    {
        var tenantId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        var json = """{"Policies":[]}""";

        var updated = TenantDuplicatorService.ApplyAuthorizationApiSecretKey(
            json,
            "new-authorization-secret-key-32ch",
            tenantId);

        var root = JsonNode.Parse(updated)!.AsObject();
        Assert.Equal("new-authorization-secret-key-32ch", root["TokenSettings"]!["SecretKey"]!.GetValue<string>());
        Assert.Equal(tenantId.ToString(), root["TokenSettings"]!["Issuer"]!.GetValue<string>());
        Assert.Equal($"api-audience-{tenantId:D}", root["TokenSettings"]!["Audience"]!.GetValue<string>());
    }

    [Fact]
    public void BuildAuthorizationSettingsJson_CreatesApiTokenSettings()
    {
        var tenantId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        const string secretKey = "new-authorization-secret-key-32ch";

        var json = TenantDuplicatorService.BuildAuthorizationSettingsJson(secretKey, tenantId);

        var root = JsonNode.Parse(json)!.AsObject();
        Assert.Equal(secretKey, root["TokenSettings"]!["SecretKey"]!.GetValue<string>());
        Assert.Equal(tenantId.ToString(), root["TokenSettings"]!["Issuer"]!.GetValue<string>());
    }

    [Fact]
    public void ApplyInternalServiceAuthSecrets_ReplacesSecretKeyAndServiceApiKeysByEmail()
    {
        var json = """
            {"SecretKey":"old-internal-secret-key-32chars!","Issuer":"iss","Audience":"aud","Services":[{"Email":"web@example.com","ApiKey":"old-api-key-aaaaaaaaaaaaaaaa"},{"Email":"api@example.com","ApiKey":"old-api-key-bbbbbbbbbbbbbbbb"}]}
            """;

        var updated = TenantDuplicatorService.ApplyInternalServiceAuthSecrets(
            json,
            "new-internal-secret-key-32chars!",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["web@example.com"] = "new-web-api-key-value-32chars!!!!",
                ["api@example.com"] = "new-api-api-key-value-32chars!!!!"
            });

        var root = JsonNode.Parse(updated)!.AsObject();
        Assert.Equal("new-internal-secret-key-32chars!", root["SecretKey"]!.GetValue<string>());

        var services = root["Services"]!.AsArray();
        Assert.Equal(2, services.Count);
        Assert.Equal("web@example.com", services[0]!["Email"]!.GetValue<string>());
        Assert.Equal("new-web-api-key-value-32chars!!!!", services[0]!["ApiKey"]!.GetValue<string>());
        Assert.Equal("new-api-api-key-value-32chars!!!!", services[1]!["ApiKey"]!.GetValue<string>());
    }

    [Fact]
    public void ApplyInternalServiceAuthSecrets_Throws_WhenServiceApiKeyMissing()
    {
        var json = """
            {"SecretKey":"old","Issuer":"iss","Audience":"aud","Services":[{"Email":"svc@example.com","ApiKey":"old-key"}]}
            """;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            TenantDuplicatorService.ApplyInternalServiceAuthSecrets(
                json,
                "shared-internal-secret-key-32chars",
                new Dictionary<string, string>()));

        Assert.Contains("svc@example.com", ex.Message);
    }

    [Fact]
    public void BuildTestAuthenticationSettingsJson_EnablesTestAuthWithTenantScopedIssuerAndAudience()
    {
        var tenantId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        const string signingKey = "test-signing-key-value-32chars!!";

        var json = TenantDuplicatorService.BuildTestAuthenticationSettingsJson(tenantId, signingKey);

        var root = JsonNode.Parse(json)!.AsObject();
        Assert.True(root["Enabled"]!.GetValue<bool>());
        Assert.Equal(signingKey, root["JwtSigningKey"]!.GetValue<string>());
        Assert.Equal(tenantId.ToString(), root["JwtIssuer"]!.GetValue<string>());
        Assert.Equal($"test-audience-{tenantId:D}", root["JwtAudience"]!.GetValue<string>());
    }

    [Fact]
    public void BuildTestAuthenticationSchemeJson_UsesTestAuthenticationScheme()
    {
        var json = TenantDuplicatorService.BuildTestAuthenticationSchemeJson();
        var root = JsonNode.Parse(json)!.AsObject();
        Assert.Equal("TestAuthentication", root["Scheme"]!.GetValue<string>());
    }
}
