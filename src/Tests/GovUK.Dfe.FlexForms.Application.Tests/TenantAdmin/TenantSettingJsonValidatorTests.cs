using GovUK.Dfe.FlexForms.Application.TenantAdmin.Validation;

namespace GovUK.Dfe.FlexForms.Application.Tests.TenantAdmin;

public class TenantSettingJsonValidatorTests
{
    [Fact]
    public void Validate_ShouldAcceptUnknownCategory_WhenJsonIsObject()
    {
        var errors = TenantSettingJsonValidator.Validate("Layout", "Web", """{"Theme":"dark"}""");
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ShouldRequireTestAuthFields_WhenEnabled()
    {
        var errors = TenantSettingJsonValidator.Validate(
            "TestAuthentication",
            "Shared",
            """{"Enabled":true}""");

        Assert.Contains(errors, e => e.Contains("JwtSigningKey"));
        Assert.Contains(errors, e => e.Contains("JwtIssuer"));
    }

    [Fact]
    public void Validate_ShouldAcceptEmptyDfESignInObject_InStrictMode()
    {
        // Seeded stubs may be incomplete until configured.
        var errors = TenantSettingJsonValidator.Validate("DfESignIn", "Shared", """{}""");
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ShouldAcceptAllowedHostsArray()
    {
        var errors = TenantSettingJsonValidator.Validate("AllowedHosts", "Api", """["localhost"]""");
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ShouldAcceptStringBoolean_ForEntraSsoEnabled()
    {
        var errors = TenantSettingJsonValidator.Validate(
            "EntraSso",
            "Api",
            """{"Enabled":"false"}""");

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ShouldAcceptStringBooleanTrue_AndRequireFields()
    {
        var errors = TenantSettingJsonValidator.Validate(
            "EntraSso",
            "Api",
            """{"Enabled":"true"}""");

        Assert.Contains(errors, e => e.Contains("TenantId"));
        Assert.Contains(errors, e => e.Contains("ClientId"));
    }

    [Fact]
    public void Validate_ShouldAcceptFeatureManagementBoolean()
    {
        var errors = TenantSettingJsonValidator.Validate("FeatureManagement", "Api", "true");
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ShouldAcceptFeatureManagementObject()
    {
        var errors = TenantSettingJsonValidator.Validate(
            "FeatureManagement",
            "Api",
            """{"MyFeature":true,"OtherFeature":"false"}""");
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ShouldAcceptSecretPlaceholderObject()
    {
        var errors = TenantSettingJsonValidator.Validate("Authorization", "Api", """{"__SECRET__":true}""");
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ShouldAcceptNumericRoot_InLenientMode()
    {
        var errors = TenantSettingJsonValidator.Validate(
            "SomeSetting",
            "Api",
            "42",
            TenantSettingValidationMode.Lenient);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ShouldAcceptNullRoot_InLenientMode()
    {
        var errors = TenantSettingJsonValidator.Validate(
            "SomeSetting",
            "Api",
            "null",
            TenantSettingValidationMode.Lenient);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_Lenient_ShouldAcceptIncompleteAuthObject()
    {
        // Import must round-trip whatever was exported, even if incomplete.
        var errors = TenantSettingJsonValidator.Validate(
            "TestAuthentication",
            "Shared",
            """{"Enabled":true}""",
            TenantSettingValidationMode.Lenient);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ShouldAcceptNamedConnectionStrings_WithoutDefault()
    {
        var errors = TenantSettingJsonValidator.Validate(
            "ConnectionStrings",
            "Api",
            """{"TenantConfig":"Server=.;Database=x"}""");

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ShouldRejectInvalidJson()
    {
        var errors = TenantSettingJsonValidator.Validate("Layout", "Web", "not-json");
        Assert.Single(errors);
        Assert.Contains("Invalid JSON", errors[0]);
    }

    [Fact]
    public void Validate_ShouldRejectInvalidJson_EvenInLenientMode()
    {
        var errors = TenantSettingJsonValidator.Validate(
            "Layout",
            "Web",
            "not-json",
            TenantSettingValidationMode.Lenient);

        Assert.Single(errors);
        Assert.Contains("Invalid JSON", errors[0]);
    }

    [Theory]
    [InlineData("""["localhost"]""")]
    [InlineData("\"localhost\"")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("null")]
    [InlineData("""{"a":1}""")]
    public void Validate_Lenient_ShouldAcceptAnyParseableJson(string json)
    {
        var errors = TenantSettingJsonValidator.Validate(
            "Anything",
            "Api",
            json,
            TenantSettingValidationMode.Lenient);

        Assert.Empty(errors);
    }
}
