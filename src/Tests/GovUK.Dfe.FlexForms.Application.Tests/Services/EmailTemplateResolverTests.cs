using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class EmailTemplateResolverTests
{
    private readonly ILogger<EmailTemplateResolver> _logger;
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly EmailTemplateResolver _resolver;

    public EmailTemplateResolverTests()
    {
        _logger = Substitute.For<ILogger<EmailTemplateResolver>>();
        _tenantContextAccessor = Substitute.For<ITenantContextAccessor>();

        var tenantSettings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApplicationTemplates:HostMappings:transfer"] = "9A4E9C58-9135-468C-B154-7B966F7ACFB7",
                ["ApplicationTemplates:HostMappings:sigchange"] = "B2F8E7D4-2C46-4A91-8E73-9D5A1F4B6C89",
                ["EmailTemplates:Transfer:ApplicationSubmitted"] = "a4188604-0053-4d77-9ad2-720f6fbbdf0a",
                ["EmailTemplates:Transfer:ContributorInvited"] = "b5299715-1164-5e88-ae3f-8c077g8bdg1b",
                ["EmailTemplates:SigChange:ApplicationSubmitted"] = "c6300826-2275-6f99-bf40-9d188h9ceh2c",
                ["EmailTemplates:SigChange:ContributorInvited"] = "d7411937-3386-7g00-cg51-ae299i0dfj3d",
            })
            .Build();

        var tenantConfig = new TenantConfiguration(
            Guid.Parse("11111111-1111-4111-8111-111111111111"),
            "TestTenant",
            tenantSettings,
            Array.Empty<string>());

        _tenantContextAccessor.CurrentTenant.Returns(tenantConfig);

        _resolver = new EmailTemplateResolver(_tenantContextAccessor, _logger);
    }

    [Fact]
    public async Task ResolveEmailTemplateAsync_ShouldReturnCorrectTemplate_ForTransferApplicationSubmitted()
    {
        // Arrange
        var templateId = new TemplateId(Guid.Parse("9A4E9C58-9135-468C-B154-7B966F7ACFB7"));
        var emailType = "ApplicationSubmitted";

        // Act
        var result = await _resolver.ResolveEmailTemplateAsync(templateId, emailType);

        // Assert
        Assert.Equal("a4188604-0053-4d77-9ad2-720f6fbbdf0a", result);
    }

    [Fact]
    public async Task ResolveEmailTemplateAsync_ShouldReturnCorrectTemplate_ForSigChangeApplicationSubmitted()
    {
        // Arrange
        var templateId = new TemplateId(Guid.Parse("B2F8E7D4-2C46-4A91-8E73-9D5A1F4B6C89"));
        var emailType = "ApplicationSubmitted";

        // Act
        var result = await _resolver.ResolveEmailTemplateAsync(templateId, emailType);

        // Assert
        Assert.Equal("c6300826-2275-6f99-bf40-9d188h9ceh2c", result);
    }

    [Fact]
    public async Task ResolveEmailTemplateAsync_ShouldReturnCorrectTemplate_ForTransferContributorInvited()
    {
        // Arrange
        var templateId = new TemplateId(Guid.Parse("9A4E9C58-9135-468C-B154-7B966F7ACFB7"));
        var emailType = "ContributorInvited";

        // Act
        var result = await _resolver.ResolveEmailTemplateAsync(templateId, emailType);

        // Assert
        Assert.Equal("b5299715-1164-5e88-ae3f-8c077g8bdg1b", result);
    }

    [Fact]
    public async Task ResolveEmailTemplateAsync_ShouldReturnNull_ForUnknownTemplate()
    {
        // Arrange
        var templateId = new TemplateId(Guid.Parse("00000000-0000-0000-0000-000000000000"));
        var emailType = "ApplicationSubmitted";

        // Act
        var result = await _resolver.ResolveEmailTemplateAsync(templateId, emailType);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveEmailTemplateAsync_ShouldReturnNull_ForUnknownEmailType()
    {
        // Arrange
        var templateId = new TemplateId(Guid.Parse("9A4E9C58-9135-468C-B154-7B966F7ACFB7"));
        var emailType = "UnknownEmailType";

        // Act
        var result = await _resolver.ResolveEmailTemplateAsync(templateId, emailType);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetApplicationTypeAsync_ShouldReturnCorrectType_ForTransfer()
    {
        // Arrange
        var templateId = new TemplateId(Guid.Parse("9A4E9C58-9135-468C-B154-7B966F7ACFB7"));

        // Act
        var result = await _resolver.GetApplicationTypeAsync(templateId);

        // Assert — HostMappings "transfer" → candidate "Transfer", then EmailTemplates key casing
        Assert.Equal("Transfer", result);
    }

    [Fact]
    public async Task ResolveEmailTemplateAsync_ShouldFallBackToSoleEmailTemplatesKey_WhenHostMappingMissing()
    {
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailTemplates:ProductOne:BugReportInternal"] = "8b51948c-d7d1-4a26-8ea6-f4a78a27ce5a",
            })
            .Build();

        var tenant = new TenantConfiguration(
            Guid.Parse("11111111-1111-4111-8111-111111111111"),
            "AnyTenant",
            settings,
            Array.Empty<string>());

        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.CurrentTenant.Returns(tenant);
        var resolver = new EmailTemplateResolver(accessor, _logger);

        var result = await resolver.ResolveEmailTemplateAsync(
            new TemplateId(Guid.Parse("9A4E9C58-9135-468C-B154-7B966F7ACFB7")),
            "BugReportInternal");

        Assert.Equal("8b51948c-d7d1-4a26-8ea6-f4a78a27ce5a", result);
    }

    [Fact]
    public async Task ResolveEmailTemplateAsync_ShouldResolve_WhenHostMappingKeyIsFqdn()
    {
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApplicationTemplates:HostMappings:productone.dev.example.gov.uk"] =
                    "9A4E9C58-9135-468C-B154-7B966F7ACFB7",
                ["EmailTemplates:Productone:BugReportInternal"] = "8b51948c-d7d1-4a26-8ea6-f4a78a27ce5a",
            })
            .Build();

        var tenant = new TenantConfiguration(
            Guid.Parse("11111111-1111-4111-8111-111111111111"),
            "AnyTenant",
            settings,
            Array.Empty<string>());

        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.CurrentTenant.Returns(tenant);
        var resolver = new EmailTemplateResolver(accessor, _logger);

        var result = await resolver.ResolveEmailTemplateAsync(
            new TemplateId(Guid.Parse("9A4E9C58-9135-468C-B154-7B966F7ACFB7")),
            "BugReportInternal");

        Assert.Equal("8b51948c-d7d1-4a26-8ea6-f4a78a27ce5a", result);
    }

    [Fact]
    public void ConvertHostMappingToApplicationType_ShouldUseFirstFqdnLabel()
    {
        Assert.Equal("Transfer", EmailTemplateResolver.ConvertHostMappingToApplicationType("transfer"));
        Assert.Equal("Productone",
            EmailTemplateResolver.ConvertHostMappingToApplicationType("productone.dev-flexforms.example.gov.uk"));
        Assert.Equal("Sigchange", EmailTemplateResolver.ConvertHostMappingToApplicationType("sigchange"));
    }

    [Fact]
    public async Task GetApplicationTypeAsync_ShouldReturnCorrectType_ForSigChange()
    {
        // Arrange
        var templateId = new TemplateId(Guid.Parse("B2F8E7D4-2C46-4A91-8E73-9D5A1F4B6C89"));

        // Act
        var result = await _resolver.GetApplicationTypeAsync(templateId);

        // Assert
        Assert.Equal("SigChange", result);
    }

    [Fact]
    public async Task GetApplicationTypeAsync_ShouldReturnNull_ForUnknownTemplate()
    {
        // Arrange
        var templateId = new TemplateId(Guid.Parse("00000000-0000-0000-0000-000000000000"));

        // Act
        var result = await _resolver.GetApplicationTypeAsync(templateId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveEmailTemplateAsync_ShouldLogWarning_WhenTemplateNotFound()
    {
        // Arrange
        var templateId = new TemplateId(Guid.Parse("00000000-0000-0000-0000-000000000000"));
        var emailType = "ApplicationSubmitted";

        // Act
        await _resolver.ResolveEmailTemplateAsync(templateId, emailType);

        // Assert
        _logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Template ID 00000000-0000-0000-0000-000000000000 not found in host mappings")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task ResolveEmailTemplateAsync_ShouldLogWarning_WhenEmailTypeNotFound()
    {
        // Arrange
        var templateId = new TemplateId(Guid.Parse("9A4E9C58-9135-468C-B154-7B966F7ACFB7"));
        var emailType = "UnknownEmailType";

        // Act
        await _resolver.ResolveEmailTemplateAsync(templateId, emailType);

        // Assert
        _logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Could not find email template for application type Transfer and email type UnknownEmailType")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task ResolveEmailTemplateAsync_ShouldLogDebug_WhenTemplateResolved()
    {
        // Arrange
        var templateId = new TemplateId(Guid.Parse("9A4E9C58-9135-468C-B154-7B966F7ACFB7"));
        var emailType = "ApplicationSubmitted";

        // Act
        await _resolver.ResolveEmailTemplateAsync(templateId, emailType);

        // Assert
        _logger.Received(1).Log(
            LogLevel.Debug,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Resolved email template a4188604-0053-4d77-9ad2-720f6fbbdf0a for application type Transfer and email type ApplicationSubmitted")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task ResolveEmailTemplateAsync_ShouldThrow_WhenNoTenantContext()
    {
        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.CurrentTenant.Returns((TenantConfiguration?)null);
        var resolver = new EmailTemplateResolver(accessor, _logger);

        var templateId = new TemplateId(Guid.Parse("9A4E9C58-9135-468C-B154-7B966F7ACFB7"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveEmailTemplateAsync(templateId, "ApplicationSubmitted"));
    }

    [Fact]
    public async Task ResolveEmailTemplateAsync_ShouldUseTenantSpecificConfig()
    {
        var tenantBSettings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApplicationTemplates:HostMappings:transfer"] = "9A4E9C58-9135-468C-B154-7B966F7ACFB7",
                ["EmailTemplates:Transfer:ApplicationSubmitted"] = "different-tenant-template-id",
            })
            .Build();

        var tenantB = new TenantConfiguration(
            Guid.Parse("22222222-2222-4222-8222-222222222222"),
            "TenantB",
            tenantBSettings,
            Array.Empty<string>());

        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.CurrentTenant.Returns(tenantB);

        var resolver = new EmailTemplateResolver(accessor, _logger);
        var templateId = new TemplateId(Guid.Parse("9A4E9C58-9135-468C-B154-7B966F7ACFB7"));

        var result = await resolver.ResolveEmailTemplateAsync(templateId, "ApplicationSubmitted");

        Assert.Equal("different-tenant-template-id", result);
    }
}
