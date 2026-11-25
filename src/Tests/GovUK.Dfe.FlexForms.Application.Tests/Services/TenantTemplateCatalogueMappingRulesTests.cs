using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.NSubstitute;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

/// <summary>
/// Adds to <c>TenantTemplateCatalogueTests</c>: every mapping key, mappings to missing templates, bad values,
/// no tenant context and <c>ContainsAsync</c>.
/// </summary>
public class TenantTemplateCatalogueMappingRulesTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherTenantId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly UserId Creator = new(Guid.NewGuid());

    private static readonly Guid Legacy = Guid.Parse("a0000000-0000-4000-8000-000000000001");
    private static readonly Guid OwnedAndMapped = Guid.Parse("a0000000-0000-4000-8000-000000000002");
    private static readonly Guid OwnedOnly = Guid.Parse("a0000000-0000-4000-8000-000000000003");
    private static readonly Guid Foreign = Guid.Parse("a0000000-0000-4000-8000-000000000004");
    private static readonly Guid Missing = Guid.Parse("a0000000-0000-4000-8000-000000000005");
    private static readonly Guid LegacySingle = Guid.Parse("a0000000-0000-4000-8000-000000000006");
    private static readonly Guid LegacyTemplateId = Guid.Parse("a0000000-0000-4000-8000-000000000007");

    private readonly IEaRepository<Template> _templates = Substitute.For<IEaRepository<Template>>();
    private readonly ITenantContextAccessor _tenantContext = Substitute.For<ITenantContextAccessor>();

    public TenantTemplateCatalogueMappingRulesTests()
    {
        var templates = new List<Template>
        {
            Template(Legacy, tenantId: null),
            Template(OwnedAndMapped, TenantId),
            Template(OwnedOnly, TenantId),
            Template(Foreign, OtherTenantId),
            Template(LegacySingle, tenantId: null),
            Template(LegacyTemplateId, tenantId: null),
        }.BuildMockDbSet();
        _templates.Query().Returns(templates);
    }

    [Fact]
    public async Task Catalogue_IsClaimableMappingsPlusOwnedTemplates()
    {
        UseTenant(new Dictionary<string, string?>
        {
            ["ApplicationTemplates:HostMappings:Transfers"] = Legacy.ToString(),
            ["ApplicationTemplates:HostMappings:Owned"] = OwnedAndMapped.ToString(),
            ["Template:HostMappings:Stolen"] = Foreign.ToString(),
            ["Template:HostMappings:Gone"] = Missing.ToString(),
            ["ApplicationTemplates:TemplateId"] = LegacySingle.ToString(),
            ["Template:Id"] = LegacyTemplateId.ToString(),
        });

        var ids = await Catalogue().GetTemplateIdsAsync();

        Assert.Equal(
            new[] { Legacy, OwnedAndMapped, LegacySingle, LegacyTemplateId, OwnedOnly }.Order(),
            ids.Select(id => id.Value).Order());
    }

    [Fact]
    public async Task BlankInvalidAndDuplicateMappings_AreIgnored()
    {
        UseTenant(new Dictionary<string, string?>
        {
            ["ApplicationTemplates:HostMappings:Blank"] = " ",
            ["ApplicationTemplates:HostMappings:Bad"] = "not-a-guid",
            ["ApplicationTemplates:HostMappings:A"] = Legacy.ToString(),
            ["Template:HostMappings:B"] = Legacy.ToString(),
            ["ApplicationTemplates:TemplateId"] = "also-bad",
            ["Template:Id"] = OwnedOnly.ToString(),
        });

        var ids = await Catalogue().GetTemplateIdsAsync();

        // OwnedAndMapped and OwnedOnly are owned by the tenant, so they're listed without a mapping.
        Assert.Equal(new[] { Legacy, OwnedAndMapped, OwnedOnly }.Order(), ids.Select(id => id.Value).Order());
    }

    [Fact]
    public async Task NoTenantContext_IsAnEmptyCatalogue()
    {
        _tenantContext.CurrentTenant.Returns((TenantConfiguration?)null);

        var catalogue = Catalogue();

        Assert.Empty(await catalogue.GetTemplateIdsAsync());
        Assert.False(await catalogue.ContainsAsync(new TemplateId(OwnedOnly)));
    }

    [Fact]
    public async Task Contains_IsTrueOnlyForCatalogueTemplates()
    {
        UseTenant(new Dictionary<string, string?> { ["Template:Id"] = Legacy.ToString() });

        var catalogue = Catalogue();

        Assert.True(await catalogue.ContainsAsync(new TemplateId(Legacy)));
        Assert.True(await catalogue.ContainsAsync(new TemplateId(OwnedOnly)));
        Assert.False(await catalogue.ContainsAsync(new TemplateId(LegacySingle)));
        Assert.False(await catalogue.ContainsAsync(new TemplateId(Foreign)));
    }

    private void UseTenant(Dictionary<string, string?> settings) =>
        _tenantContext.CurrentTenant.Returns(new TenantConfiguration(
            TenantId, "Transfers", new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), []));

    private TenantTemplateCatalogue Catalogue() => new(_templates, _tenantContext, NullLogger<TenantTemplateCatalogue>.Instance);

    private static Template Template(Guid id, Guid? tenantId) =>
        new(new TemplateId(id), $"Template {id}", DateTime.UtcNow, Creator, tenantId: tenantId);
}
