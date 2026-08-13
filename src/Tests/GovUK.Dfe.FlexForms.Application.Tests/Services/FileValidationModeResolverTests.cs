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
}
