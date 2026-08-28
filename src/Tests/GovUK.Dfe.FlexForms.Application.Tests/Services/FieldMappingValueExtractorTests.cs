using System.Text.Json;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Models.EventMapping;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class FieldMappingValueExtractorTests
{
    private readonly FieldMappingValueExtractor _extractor =
        new(Substitute.For<ILogger<FieldMappingValueExtractor>>());

    [Fact]
    public void ExtractValue_ShouldReadFirstTrustName_FromWrappedCollectionString()
    {
        var collectionJson = """[{"id":"trust-1","trustsSearch-field-flow":"{\"ukprn\":\"12345678\",\"name\":\"Oak Trust\"}"}]""";
        var formData = new Dictionary<string, object>
        {
            ["detailsOfOutgoingTrusts"] = JsonDocument.Parse(JsonSerializer.Serialize(collectionJson)).RootElement
        };

        var mapping = new FieldMapping
        {
            SourceType = FieldMappingSourceType.Collection,
            CollectionMapping = new CollectionMapping
            {
                SourceCollectionFieldId = "detailsOfOutgoingTrusts",
                ExtractFirst = true,
                NestedPath = "trustsSearch-field-flow.name"
            }
        };

        var value = _extractor.ExtractValue(mapping, formData, Guid.Empty, string.Empty, null);

        Assert.Equal("Oak Trust", value);
    }

    [Fact]
    public void ExtractValue_ShouldReadFirstTrustName_FromStoredEnvelopeObject()
    {
        var collectionJson = """[{"id":"trust-1","trustsSearch-field-flow":"{\"ukprn\":\"12345678\",\"name\":\"Oak Trust\"}"}]""";
        var envelope = JsonDocument.Parse($$"""
            {
              "question": "Outgoing trusts",
              "value": {{JsonSerializer.Serialize(collectionJson)}},
              "completed": true,
              "dataType": "array"
            }
            """).RootElement;

        var formData = new Dictionary<string, object>
        {
            ["detailsOfOutgoingTrusts"] = envelope
        };

        var mapping = new FieldMapping
        {
            SourceType = FieldMappingSourceType.Collection,
            CollectionMapping = new CollectionMapping
            {
                SourceCollectionFieldId = "detailsOfOutgoingTrusts",
                ExtractFirst = true,
                NestedPath = "trustsSearch-field-flow.name"
            }
        };

        var value = _extractor.ExtractValue(mapping, formData, Guid.Empty, string.Empty, null);

        Assert.Equal("Oak Trust", value);
    }

    [Fact]
    public void ExtractValue_ShouldReturnEmptyString_WhenScalarCollectionMappingFails()
    {
        var formData = new Dictionary<string, object>
        {
            ["detailsOfOutgoingTrusts"] = JsonDocument.Parse("""{"not":"an-array"}""").RootElement
        };

        var mapping = new FieldMapping
        {
            SourceType = FieldMappingSourceType.Collection,
            CollectionMapping = new CollectionMapping
            {
                SourceCollectionFieldId = "detailsOfOutgoingTrusts",
                ExtractFirst = true,
                NestedPath = "trustsSearch-field-flow.name"
            }
        };

        var value = _extractor.ExtractValue(mapping, formData, Guid.Empty, string.Empty, null);

        Assert.Equal(string.Empty, value);
    }
}
