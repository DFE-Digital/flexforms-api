using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class EmailPlaceholderMappingProviderTests
{
    private static EmailPlaceholderMappingProvider CreateProvider(IConfigurationRoot settings)
    {
        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.CurrentTenant.Returns(new TenantConfiguration(Guid.NewGuid(), "t", settings, []));
        return new EmailPlaceholderMappingProvider(
            accessor,
            Substitute.For<ILogger<EmailPlaceholderMappingProvider>>());
    }

    [Fact]
    public async Task GetMappingAsync_ReturnsExactMatch()
    {
        var templateId = Guid.NewGuid().ToString();
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"EmailPlaceholderMappings:{templateId}:ApplicationSubmitted:mappingId"] = "email-v1",
                [$"EmailPlaceholderMappings:{templateId}:ApplicationSubmitted:eventType"] = "ApplicationSubmitted",
                [$"EmailPlaceholderMappings:{templateId}:ApplicationSubmitted:fieldMappings:AcademyName:sourceType"] = "ComplexFieldProperty",
                [$"EmailPlaceholderMappings:{templateId}:ApplicationSubmitted:fieldMappings:AcademyName:sourceFieldId"] = "academiesSearch",
                [$"EmailPlaceholderMappings:{templateId}:ApplicationSubmitted:fieldMappings:AcademyName:nestedPath"] = "name",
            })
            .Build();

        var mapping = await CreateProvider(settings).GetMappingAsync(templateId, "ApplicationSubmitted");

        Assert.NotNull(mapping);
        Assert.Equal("email-v1", mapping.MappingId);
        Assert.True(mapping.FieldMappings.ContainsKey("AcademyName"));
        Assert.Equal("academiesSearch", mapping.FieldMappings["AcademyName"].SourceFieldId);
        Assert.Equal("name", mapping.FieldMappings["AcademyName"].NestedPath);
    }

    [Fact]
    public async Task GetMappingAsync_FallsBackToSiblingTemplateKey()
    {
        var apiGuid = Guid.NewGuid().ToString();
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailPlaceholderMappings:form-001:ApplicationSubmitted:mappingId"] = "legacy-email-v1",
                ["EmailPlaceholderMappings:form-001:ApplicationSubmitted:eventType"] = "ApplicationSubmitted",
                ["EmailPlaceholderMappings:form-001:ApplicationSubmitted:fieldMappings:user_full_name:sourceType"] = "Metadata",
                ["EmailPlaceholderMappings:form-001:ApplicationSubmitted:fieldMappings:user_full_name:sourceFieldId"] = "submittedByFullName",
            })
            .Build();

        var mapping = await CreateProvider(settings).GetMappingAsync(apiGuid, "ApplicationSubmitted");

        Assert.NotNull(mapping);
        Assert.Equal("legacy-email-v1", mapping.MappingId);
    }

    [Fact]
    public async Task GetMappingAsync_ReturnsNull_WhenMissing()
    {
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var mapping = await CreateProvider(settings).GetMappingAsync(Guid.NewGuid().ToString(), "ApplicationSubmitted");

        Assert.Null(mapping);
    }

    [Fact]
    public async Task GetMappingAsync_ReturnsNull_WhenNoTenant()
    {
        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.CurrentTenant.Returns((TenantConfiguration?)null);
        var provider = new EmailPlaceholderMappingProvider(
            accessor,
            Substitute.For<ILogger<EmailPlaceholderMappingProvider>>());

        var mapping = await provider.GetMappingAsync("form-001", "ApplicationSubmitted");

        Assert.Null(mapping);
    }
}
