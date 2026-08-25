using GovUK.Dfe.FlexForms.Application.Options;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Models.EventMapping;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class EmailPersonalisationBuilderTests
{
    private readonly IEmailPlaceholderMappingProvider _mappingProvider = Substitute.For<IEmailPlaceholderMappingProvider>();
    private readonly IFieldMappingValueExtractor _valueExtractor;
    private readonly EmailPersonalisationBuilder _builder;

    public EmailPersonalisationBuilderTests()
    {
        _valueExtractor = new FieldMappingValueExtractor(Substitute.For<ILogger<FieldMappingValueExtractor>>());
        _builder = new EmailPersonalisationBuilder(
            _mappingProvider,
            _valueExtractor,
            Substitute.For<ILogger<EmailPersonalisationBuilder>>());
    }

    [Fact]
    public async Task BuildAsync_ReturnsBaseline_WhenNoMapping()
    {
        _mappingProvider.GetMappingAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((EventFieldMapping?)null);

        var baseline = new Dictionary<string, object>
        {
            ["user_full_name"] = "Alice",
            ["application_reference"] = "APP-1"
        };

        var result = await _builder.BuildAsync(
            "form-001",
            EmailTypes.ApplicationSubmitted,
            Guid.NewGuid(),
            "APP-1",
            baseline,
            new Dictionary<string, object>());

        Assert.Equal("Alice", result["user_full_name"]);
        Assert.Equal("APP-1", result["application_reference"]);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task BuildAsync_OverlaysMappedFormField()
    {
        _mappingProvider.GetMappingAsync("form-001", EmailTypes.ApplicationSubmitted, Arg.Any<CancellationToken>())
            .Returns(new EventFieldMapping
            {
                MappingId = "email-v1",
                EventType = EmailTypes.ApplicationSubmitted,
                FieldMappings = new Dictionary<string, FieldMapping>
                {
                    ["AcademyName"] = new FieldMapping
                    {
                        SourceType = FieldMappingSourceType.ComplexFieldProperty,
                        SourceFieldId = "academiesSearch",
                        NestedPath = "name"
                    }
                }
            });

        var baseline = new Dictionary<string, object>
        {
            ["user_full_name"] = "Alice",
            ["application_reference"] = "APP-1"
        };

        var formData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["academiesSearch"] = """{"name":"Oak Academy","urn":"123"}"""
        };

        var result = await _builder.BuildAsync(
            "form-001",
            EmailTypes.ApplicationSubmitted,
            Guid.NewGuid(),
            "APP-1",
            baseline,
            formData);

        Assert.Equal("Alice", result["user_full_name"]);
        Assert.Equal("Oak Academy", result["AcademyName"]);
    }

    [Fact]
    public async Task BuildAsync_MappedKeyOverridesBaseline()
    {
        _mappingProvider.GetMappingAsync("form-001", EmailTypes.ApplicationSubmitted, Arg.Any<CancellationToken>())
            .Returns(new EventFieldMapping
            {
                MappingId = "email-v1",
                EventType = EmailTypes.ApplicationSubmitted,
                FieldMappings = new Dictionary<string, FieldMapping>
                {
                    ["user_full_name"] = new FieldMapping
                    {
                        SourceType = FieldMappingSourceType.Metadata,
                        SourceFieldId = PlatformEventMetadataKeys.SubmittedByFullName
                    }
                }
            });

        var baseline = new Dictionary<string, object>
        {
            ["user_full_name"] = "Baseline Name"
        };

        var metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [PlatformEventMetadataKeys.SubmittedByFullName] = "Mapped Name"
        };

        var result = await _builder.BuildAsync(
            "form-001",
            EmailTypes.ApplicationSubmitted,
            Guid.NewGuid(),
            "APP-1",
            baseline,
            new Dictionary<string, object>(),
            metadata);

        Assert.Equal("Mapped Name", result["user_full_name"]);
    }
}
