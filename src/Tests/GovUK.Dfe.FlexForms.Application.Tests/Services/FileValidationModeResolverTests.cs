using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class FileValidationModeResolverTests
{
    [Fact]
    public void Resolve_ReturnsOff_WhenNoTenant()
    {
        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.CurrentTenant.Returns((TenantConfiguration?)null);

        var mode = new FileValidationModeResolver(accessor).Resolve(Guid.NewGuid());

        Assert.Equal(FileValidationMode.Off, mode);
    }

    [Fact]
    public void Resolve_ReturnsTemplateOverride()
    {
        var templateId = Guid.NewGuid();
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileValidation:DefaultMode"] = "FailOnInvalid",
                [$"FileValidation:Templates:{templateId}"] = "RequirePassed"
            })
            .Build();

        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.CurrentTenant.Returns(new TenantConfiguration(Guid.NewGuid(), "t", settings, []));

        var mode = new FileValidationModeResolver(accessor).Resolve(templateId);

        Assert.Equal(FileValidationMode.RequirePassed, mode);
    }

    [Fact]
    public void Resolve_ReturnsDefaultMode_WhenTemplateMissing()
    {
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileValidation:DefaultMode"] = "FailOnInvalid"
            })
            .Build();

        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.CurrentTenant.Returns(new TenantConfiguration(Guid.NewGuid(), "t", settings, []));

        var mode = new FileValidationModeResolver(accessor).Resolve(Guid.NewGuid());

        Assert.Equal(FileValidationMode.FailOnInvalid, mode);
    }

    [Fact]
    public void IsExtensionSubjectToValidation_ReturnsTrue_WhenExtensionsMissing()
    {
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileValidation:DefaultMode"] = "RequirePassed"
            })
            .Build();

        var resolver = CreateResolver(settings);

        Assert.True(resolver.IsExtensionSubjectToValidation("photo.png"));
        Assert.True(resolver.IsExtensionSubjectToValidation("budget.xlsx"));
    }

    [Fact]
    public void IsExtensionSubjectToValidation_FiltersByConfiguredExtensions()
    {
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileValidation:DefaultMode"] = "RequirePassed",
                ["FileValidation:Extensions:0"] = ".xlsx",
                ["FileValidation:Extensions:1"] = "xls"
            })
            .Build();

        var resolver = CreateResolver(settings);

        Assert.True(resolver.IsExtensionSubjectToValidation("budget.XLSX"));
        Assert.True(resolver.IsExtensionSubjectToValidation("legacy.xls"));
        Assert.False(resolver.IsExtensionSubjectToValidation("photo.png"));
        Assert.False(resolver.IsExtensionSubjectToValidation("scan.jpeg"));
        Assert.False(resolver.IsExtensionSubjectToValidation(null));
    }

    [Theory]
    [InlineData(".xlsx", ".xlsx")]
    [InlineData("xlsx", ".xlsx")]
    [InlineData("*.xlsx", ".xlsx")]
    [InlineData("  .XLSX  ", ".xlsx")]
    [InlineData(".", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizeExtension_NormalizesCommonForms(string? input, string? expected)
    {
        var actual = FileValidationModeResolver.NormalizeExtension(input);
        Assert.Equal(expected, actual);
    }

    private static FileValidationModeResolver CreateResolver(IConfigurationRoot settings)
    {
        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.CurrentTenant.Returns(new TenantConfiguration(Guid.NewGuid(), "t", settings, []));
        return new FileValidationModeResolver(accessor);
    }
}
