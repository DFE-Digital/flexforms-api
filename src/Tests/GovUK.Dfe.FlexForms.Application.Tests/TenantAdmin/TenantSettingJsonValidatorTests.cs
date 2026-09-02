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
    public void Validate_ShouldRequireApplicationInsightsConnectionString()
    {
        var errors = TenantSettingJsonValidator.Validate("ApplicationInsights", "Shared", """{}""");
        Assert.Contains(errors, e => e.Contains("ConnectionString"));
    }

    [Fact]
    public void Validate_ShouldAcceptApplicationInsightsConnectionString()
    {
        var errors = TenantSettingJsonValidator.Validate(
            "ApplicationInsights",
            "Shared",
            """{"ConnectionString":"InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://uksouth-1.in.applicationinsights.azure.com/"}""");

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

    [Fact]
    public void Validate_Dashboard_AcceptsOptionalTextFields()
    {
        var errors = TenantSettingJsonValidator.Validate(
            "Dashboard",
            "Web",
            """{"PageSize":50,"MainHeading":"Your visits","StartNewHint":""}""");

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_Dashboard_RejectsNonStringTextField()
    {
        var errors = TenantSettingJsonValidator.Validate(
            "Dashboard",
            "Web",
            """{"MainHeading":42}""");

        Assert.Contains(errors, e => e.Contains("MainHeading", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ApplicationPreview_AcceptsCopyAndHideFlag()
    {
        var errors = TenantSettingJsonValidator.Validate(
            "ApplicationPreview",
            "Web",
            """{"PageHeading":"Check your answers","SubmitHeading":"Submit your visit","SubmitHint":"Please confirm","SubmitButtonText":"Submit","HideSubmitSection":false}""");

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ApplicationPreview_RejectsNonBooleanHideFlag()
    {
        var errors = TenantSettingJsonValidator.Validate(
            "ApplicationPreview",
            "Web",
            """{"HideSubmitSection":"yes"}""");

        Assert.Contains(errors, e => e.Contains("HideSubmitSection", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ApplicationTemplates_RejectsInvalidHostMappingGuid()
    {
        var errors = TenantSettingJsonValidator.Validate(
            "ApplicationTemplates",
            "Api",
            """{"HostMappings":{"transfers":"not-a-guid"}}""");

        Assert.Contains(errors, e => e.Contains("HostMappings", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ApplicationTemplates_AcceptsValidHostMappings()
    {
        var errors = TenantSettingJsonValidator.Validate(
            "ApplicationTemplates",
            "Api",
            """{"HostMappings":{"transfers":"9A4E9C58-9135-468C-B154-7B966F7ACFB7"}}""");

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_FileValidation_AcceptsKnownModes()
    {
        var errors = TenantSettingJsonValidator.Validate(
            "FileValidation",
            "Shared",
            """{"DefaultMode":"RequirePassed","Extensions":[".xlsx","xls"],"Templates":{"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee":"FailOnInvalid"}}""");

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_FileValidation_RejectsUnknownMode()
    {
        var errors = TenantSettingJsonValidator.Validate(
            "FileValidation",
            "Shared",
            """{"DefaultMode":"Maybe"}""");

        Assert.Contains(errors, e => e.Contains("DefaultMode", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FileValidation_RejectsInvalidExtensions()
    {
        var errors = TenantSettingJsonValidator.Validate(
            "FileValidation",
            "Shared",
            """{"DefaultMode":"Off","Extensions":"xlsx"}""");

        Assert.Contains(errors, e => e.Contains("Extensions", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ShouldAcceptSelfRegistrationDefaultTemplateId()
    {
        var errors = TenantSettingJsonValidator.Validate(
            "SelfRegistration",
            "Shared",
            """{"DefaultTemplateId":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"}""");

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ShouldRejectInvalidSelfRegistrationDefaultTemplateId()
    {
        var errors = TenantSettingJsonValidator.Validate(
            "SelfRegistration",
            "Shared",
            """{"DefaultTemplateId":"not-a-guid"}""");

        Assert.Contains(errors, e => e.Contains("DefaultTemplateId"));
    }

    [Fact]
    public void Validate_ShouldAcceptValidFileStorageLocal()
    {
        var errors = TenantSettingJsonValidator.Validate(
            "FileStorage",
            "Api",
            """{"Provider":"Local","Local":{"BaseDirectory":"/uploads"}}""");

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ShouldRejectFileStorageWithoutProvider()
    {
        var errors = TenantSettingJsonValidator.Validate(
            "FileStorage",
            "Api",
            """{"Local":{"BaseDirectory":"/uploads"}}""");

        Assert.Contains(errors, e => e.Contains("Provider"));
    }

    [Fact]
    public void Validate_ShouldRejectInvalidFileStorageProvider()
    {
        var errors = TenantSettingJsonValidator.Validate(
            "FileStorage",
            "Api",
            """{"Provider":"S3"}""");

        Assert.Contains(errors, e => e.Contains("Local, Azure, or Hybrid", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ShouldAcceptValidEmail()
    {
        var errors = TenantSettingJsonValidator.Validate(
            "Email",
            "Api",
            """{"Provider":"GovUkNotify","ServiceSupportEmailAddress":"a@b.com","GovUkNotify":{"ApiKey":"key"}}""");

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ShouldRejectInvalidEmailProvider()
    {
        var errors = TenantSettingJsonValidator.Validate(
            "Email",
            "Api",
            """{"Provider":"SendGrid"}""");

        Assert.Contains(errors, e => e.Contains("GovUkNotify"));
    }

    [Fact]
    public void Validate_ShouldRejectDuplicateMappingId_InEventMappings()
    {
        var json = """
            {
              "form-001": {
                "TransferApplicationSubmittedEvent": {
                  "mappingId": "transfer-application-submitted-v1",
                  "eventType": "TransferApplicationSubmittedEvent",
                  "fieldMappings": { "AcademyName": { "sourceType": "DirectField", "sourceFieldId": "x" } }
                }
              },
              "9a4e9c58-9135-468c-b154-7b966f7acfb7": {
                "TransferApplicationSubmittedEvent": {
                  "mappingId": "transfer-application-submitted-v1",
                  "eventType": "TransferApplicationSubmittedEvent",
                  "fieldMappings": { "AcademyName": { "sourceType": "DirectField", "sourceFieldId": "x" } }
                }
              }
            }
            """;

        var errors = TenantSettingJsonValidator.Validate("EventMappings", "Shared", json);

        Assert.Contains(errors, e => e.Contains("mappingId 'transfer-application-submitted-v1' is duplicated", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ShouldRejectDuplicateEventType_InEventTriggers()
    {
        var json = """
            {
              "ApplicationSubmitted": [
                { "eventKind": "Typed", "eventType": "TransferApplicationSubmittedEvent", "mappingId": "map-a" },
                { "eventKind": "Typed", "eventType": "TransferApplicationSubmittedEvent", "mappingId": "map-b" }
              ]
            }
            """;

        var errors = TenantSettingJsonValidator.Validate("EventTriggers", "Shared", json);

        Assert.Contains(errors, e => e.Contains("duplicated", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, e => e.Contains("TransferApplicationSubmittedEvent", StringComparison.Ordinal));
    }
}
