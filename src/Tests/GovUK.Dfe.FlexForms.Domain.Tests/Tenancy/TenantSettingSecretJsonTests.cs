using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.FlexForms.Domain.Tenancy;

namespace GovUK.Dfe.FlexForms.Domain.Tests.Tenancy;

public class TenantSettingSecretJsonTests
{
    [Fact]
    public void Redact_ShouldReplaceSecretLeaves_AndKeepNonSecretStrings()
    {
        var json = """
            {
              "SecretKey": "super-secret",
              "Issuer": "transfers-api",
              "Audience": "transfers-web"
            }
            """;

        var result = TenantSettingSecretJson.Redact(json, "Authorization");

        Assert.Contains(TenantSettingSecretRedaction.Sentinel, result.Json);
        Assert.DoesNotContain("super-secret", result.Json);
        Assert.Contains("transfers-api", result.Json);
        var leaf = Assert.Single(result.Values);
        Assert.Equal("SecretKey", leaf.Path);
        Assert.Equal("super-secret".Length, leaf.ValueLength);
        Assert.StartsWith("sha256:", leaf.Fingerprint);
    }

    [Fact]
    public void Redact_ShouldReplaceAllConnectionStringValues()
    {
        var json = """{"DefaultConnection":"Server=.;Database=secret;"}""";

        var result = TenantSettingSecretJson.Redact(json, "ConnectionStrings");

        Assert.DoesNotContain("Server=.", result.Json);
        Assert.Equal("DefaultConnection", Assert.Single(result.Values).Path);
    }

    [Fact]
    public void Redact_ShouldRedactNestedArrayLeaves()
    {
        var json = """{"Providers":[{"Name":"Entra","KeyHash":"abc123"}]}""";

        var result = TenantSettingSecretJson.Redact(json, "AuthProviders");

        Assert.Contains("Entra", result.Json);
        Assert.DoesNotContain("abc123", result.Json);
        Assert.Equal("Providers[0]:KeyHash", Assert.Single(result.Values).Path);
    }

    [Fact]
    public void Restore_ShouldPutStoredSecretBack_WhenSentinelIsPosted()
    {
        var stored = """{"SecretKey":"keep-me","Issuer":"i"}""";
        var proposed = """{"SecretKey":"__REDACTED__","Issuer":"i"}""";

        var result = TenantSettingSecretJson.Restore(proposed, stored);

        Assert.Empty(result.Errors);
        Assert.Contains("keep-me", result.Json);
        Assert.DoesNotContain(TenantSettingSecretRedaction.Sentinel, result.Json);
    }

    [Fact]
    public void Restore_ShouldError_WhenSentinelHasNoStoredLeaf()
    {
        var stored = """{"SecretKey":"keep-me"}""";
        var proposed = """{"SecretKey":"keep-me","ApiKey":"__REDACTED__"}""";

        var result = TenantSettingSecretJson.Restore(proposed, stored);

        Assert.Contains(result.Errors, e => e.Contains("ApiKey"));
    }

    [Fact]
    public void TryGetValueAtPath_ShouldReturnNestedLeaf()
    {
        var json = """{"Providers":[{"Name":"Entra","KeyHash":"abc123"}]}""";

        Assert.True(TenantSettingSecretJson.TryGetValueAtPath(json, "Providers[0]:KeyHash", out var value));
        Assert.Equal("abc123", value);
    }

    [Fact]
    public void ToAdminDto_ShouldPassThroughNonSecretRows()
    {
        var row = new TenantSettingRow(
            Guid.NewGuid(),
            "Layout",
            "Web",
            """{"ServiceName":"Test"}""",
            false,
            DateTime.UtcNow);

        var dto = TenantSettingSecretJson.ToAdminDto(row);

        Assert.Equal(row.SettingsJson, dto.SettingsJson);
        Assert.Null(dto.RedactedValues);
    }

    [Fact]
    public void ToAdminDto_ShouldReturnPlaintext_WhenIncludePlaintextSecrets()
    {
        var row = new TenantSettingRow(
            Guid.NewGuid(),
            "Authorization",
            "Api",
            """{"SecretKey":"super-secret"}""",
            true,
            DateTime.UtcNow);

        var dto = TenantSettingSecretJson.ToAdminDto(row, includePlaintextSecrets: true);

        Assert.Contains("super-secret", dto.SettingsJson);
        Assert.Null(dto.RedactedValues);
    }
}
