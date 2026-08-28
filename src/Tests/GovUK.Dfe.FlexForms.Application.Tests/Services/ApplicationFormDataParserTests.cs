using System.Text.Json;
using GovUK.Dfe.FlexForms.Application.Services;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class ApplicationFormDataParserTests
{
    [Fact]
    public void Parse_ShouldUnwrapStoredFieldEnvelope_AndSkipTaskStatus()
    {
        var collectionJson = """[{"id":"trust-1","trustsSearch-field-flow":"{\"ukprn\":\"12345678\",\"name\":\"Oak Trust\"}"}]""";
        var responseBody = $$"""
            {
              "detailsOfOutgoingTrusts": {
                "question": "Outgoing trusts",
                "value": {{JsonSerializer.Serialize(collectionJson)}},
                "completed": true,
                "dataType": "array"
              },
              "incomingTrustTypeOfTrust": {
                "question": "Trust type",
                "value": "Multi-academy trust",
                "completed": true,
                "dataType": "string"
              },
              "TaskStatus_task-1": {
                "value": "Completed",
                "completed": true
              }
            }
            """;

        var formData = ApplicationFormDataParser.Parse(responseBody);

        Assert.False(formData.ContainsKey("TaskStatus_task-1"));
        Assert.Equal("Multi-academy trust", Assert.IsType<JsonElement>(formData["incomingTrustTypeOfTrust"]).GetString());

        var outgoingTrusts = Assert.IsType<JsonElement>(formData["detailsOfOutgoingTrusts"]);
        Assert.Equal(JsonValueKind.String, outgoingTrusts.ValueKind);
        Assert.StartsWith("[", outgoingTrusts.GetString());
    }

    [Fact]
    public void Parse_ShouldKeepLegacyFlatFieldValues()
    {
        var responseBody = """
            {
              "localAuthorityName": "Bristol"
            }
            """;

        var formData = ApplicationFormDataParser.Parse(responseBody);

        Assert.Equal("Bristol", Assert.IsType<JsonElement>(formData["localAuthorityName"]).GetString());
    }
}
