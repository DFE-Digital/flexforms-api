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

        var updated = TenantDuplicatorService.ApplyAuthorizationApiSecretKey(json, "new-authorization-secret-key-32ch");

        var root = JsonNode.Parse(updated)!.AsObject();
        Assert.Equal("new-authorization-secret-key-32ch", root["TokenSettings"]!["SecretKey"]!.GetValue<string>());
        Assert.Equal("iss", root["TokenSettings"]!["Issuer"]!.GetValue<string>());
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
}
