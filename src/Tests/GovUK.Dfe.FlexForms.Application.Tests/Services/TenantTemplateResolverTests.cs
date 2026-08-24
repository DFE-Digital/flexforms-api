using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using MockQueryable.NSubstitute;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class TenantTemplateResolverTests
{
    private static readonly TemplateId TransferTemplateId = new(Guid.Parse("9A4E9C58-9135-468C-B154-7B966F7ACFB7"));
    private static readonly TemplateId LsrpTemplateId = new(Guid.Parse("B2F8E7D4-2C46-4A91-8E73-9D5A1F4B6C89"));

    [Fact]
    public async Task ResolveListingTemplateFilterAsync_ShouldReturnRequestedTemplate_WhenItBelongsToTenant()
    {
        var resolver = CreateResolver(TransferTemplateId);

        var result = await resolver.ResolveListingTemplateFilterAsync(TransferTemplateId.Value);

        Assert.Single(result);
        Assert.Equal(TransferTemplateId, result[0]);
    }

    [Fact]
    public async Task ResolveListingTemplateFilterAsync_ShouldReturnEmpty_WhenRequestedTemplateIsOutsideTenant()
    {
        var resolver = CreateResolver(TransferTemplateId);

        var result = await resolver.ResolveListingTemplateFilterAsync(LsrpTemplateId.Value);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTemplateIdsForCurrentTenantAsync_ShouldReturnCatalogueTemplates()
    {
        var resolver = CreateResolver(TransferTemplateId);

        var templateIds = await resolver.GetTemplateIdsForCurrentTenantAsync();

        Assert.Single(templateIds);
        Assert.Equal(TransferTemplateId, templateIds[0]);
    }

    private static TenantTemplateResolver CreateResolver(params TemplateId[] templateIds)
    {
        var catalogue = Substitute.For<ITenantTemplateCatalogue>();
        catalogue.GetTemplateIdsAsync(Arg.Any<CancellationToken>())
            .Returns(templateIds.ToList().AsReadOnly());
        catalogue.ContainsAsync(Arg.Any<TemplateId>(), Arg.Any<CancellationToken>())
            .Returns(call => templateIds.Contains(call.Arg<TemplateId>()));

        return new TenantTemplateResolver(catalogue);
    }
}

public class TenantTemplateCatalogueTests
{
    private static readonly TemplateId DbTemplateId = new(Guid.Parse("9A4E9C58-9135-468C-B154-7B966F7ACFB7"));
    private static readonly TemplateId HostMappedTemplateId = new(Guid.Parse("B2F8E7D4-2C46-4A91-8E73-9D5A1F4B6C89"));

    [Fact]
    public async Task GetTemplateIdsAsync_ShouldUseHostMappings_WhenLegacyTemplateExistsInDatabase()
    {
        var createdBy = new UserId(Guid.NewGuid());
        var otherTenantTemplateId = new TemplateId(Guid.Parse("CCCCCCCC-CCCC-4CCC-8CCC-CCCCCCCCCCCC"));
        var templates = new List<Template>
        {
            // Legacy HostMapped row (TenantId null) — claimable via config.
            new(HostMappedTemplateId, "Host Mapped Template", DateTime.UtcNow, createdBy),
            new(DbTemplateId, "DB Template", DateTime.UtcNow, createdBy),
            new(otherTenantTemplateId, "Other Tenant Template", DateTime.UtcNow, createdBy, tenantId: Guid.NewGuid())
        }.AsQueryable().BuildMockDbSet();

        var templateRepo = Substitute.For<IEaRepository<Template>>();
        templateRepo.Query().Returns(templates);

        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApplicationTemplates:HostMappings:transfer"] = HostMappedTemplateId.Value.ToString()
            })
            .Build();

        var tenant = new TenantConfiguration(
            Guid.Parse("11111111-1111-4111-8111-111111111111"),
            "Transfers",
            settings,
            Array.Empty<string>());

        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.CurrentTenant.Returns(tenant);

        var catalogue = new TenantTemplateCatalogue(
            templateRepo,
            accessor,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TenantTemplateCatalogue>.Instance);

        var result = await catalogue.GetTemplateIdsAsync();

        Assert.Single(result);
        Assert.Contains(HostMappedTemplateId, result);
        Assert.DoesNotContain(DbTemplateId, result);
        Assert.DoesNotContain(otherTenantTemplateId, result);
    }

    [Fact]
    public async Task GetTemplateIdsAsync_ShouldIgnoreForeignTenantGuidInHostMappings()
    {
        var tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var createdBy = new UserId(Guid.NewGuid());
        var foreignId = new TemplateId(Guid.NewGuid());
        var templates = new List<Template>
        {
            new(foreignId, "Foreign", DateTime.UtcNow, createdBy, tenantId: Guid.NewGuid())
        }.AsQueryable().BuildMockDbSet();

        var templateRepo = Substitute.For<IEaRepository<Template>>();
        templateRepo.Query().Returns(templates);

        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApplicationTemplates:HostMappings:transfer"] = foreignId.Value.ToString()
            })
            .Build();

        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.CurrentTenant.Returns(new TenantConfiguration(tenantId, "Transfers", settings, []));

        var catalogue = new TenantTemplateCatalogue(
            templateRepo,
            accessor,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TenantTemplateCatalogue>.Instance);

        var result = await catalogue.GetTemplateIdsAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTemplateIdsAsync_ShouldCombineMappingsWithTemplatesOwnedByTenant()
    {
        var tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var createdBy = new UserId(Guid.NewGuid());
        var ownedTemplateId = new TemplateId(Guid.NewGuid());
        var otherTenantTemplateId = new TemplateId(Guid.NewGuid());
        var templates = new List<Template>
        {
            new(ownedTemplateId, "Owned Template", DateTime.UtcNow, createdBy, tenantId: tenantId),
            new(otherTenantTemplateId, "Other Tenant Template", DateTime.UtcNow, createdBy, tenantId: Guid.NewGuid()),
            // Claimable HostMapped legacy row
            new(HostMappedTemplateId, "Host Mapped", DateTime.UtcNow, createdBy)
        }.AsQueryable().BuildMockDbSet();

        var templateRepo = Substitute.For<IEaRepository<Template>>();
        templateRepo.Query().Returns(templates);

        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApplicationTemplates:HostMappings:transfer"] = HostMappedTemplateId.Value.ToString()
            })
            .Build();

        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.CurrentTenant.Returns(new TenantConfiguration(
            tenantId,
            "Transfers",
            settings,
            []));

        var catalogue = new TenantTemplateCatalogue(
            templateRepo,
            accessor,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TenantTemplateCatalogue>.Instance);

        var result = await catalogue.GetTemplateIdsAsync();

        Assert.Equal(2, result.Count);
        Assert.Contains(HostMappedTemplateId, result);
        Assert.Contains(ownedTemplateId, result);
        Assert.DoesNotContain(otherTenantTemplateId, result);
    }

    [Fact]
    public async Task GetTemplateIdsAsync_ShouldReturnEmpty_WhenNoMappingsAndNoOwnedTemplates()
    {
        var createdBy = new UserId(Guid.NewGuid());
        var templates = new List<Template>
        {
            new(DbTemplateId, "DB Template", DateTime.UtcNow, createdBy)
        }.AsQueryable().BuildMockDbSet();

        var templateRepo = Substitute.For<IEaRepository<Template>>();
        templateRepo.Query().Returns(templates);

        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var tenant = new TenantConfiguration(
            Guid.Parse("11111111-1111-4111-8111-111111111111"),
            "ClonedTenant",
            settings,
            Array.Empty<string>());

        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.CurrentTenant.Returns(tenant);

        var catalogue = new TenantTemplateCatalogue(
            templateRepo,
            accessor,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TenantTemplateCatalogue>.Instance);

        var result = await catalogue.GetTemplateIdsAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTemplateIdsAsync_ShouldReturnEmpty_WhenExplicitEmptyHostMappingsConfigured()
    {
        var createdBy = new UserId(Guid.NewGuid());
        var templates = new List<Template>
        {
            new(DbTemplateId, "DB Template", DateTime.UtcNow, createdBy)
        }.AsQueryable().BuildMockDbSet();

        var templateRepo = Substitute.For<IEaRepository<Template>>();
        templateRepo.Query().Returns(templates);

        await using var jsonStream = new MemoryStream(
            """{"ApplicationTemplates":{"HostMappings":{}}}"""u8.ToArray());
        var settings = new ConfigurationBuilder()
            .AddJsonStream(jsonStream)
            .Build();

        var tenant = new TenantConfiguration(
            Guid.Parse("11111111-1111-4111-8111-111111111111"),
            "ClonedTenant",
            settings,
            Array.Empty<string>());

        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.CurrentTenant.Returns(tenant);

        var catalogue = new TenantTemplateCatalogue(
            templateRepo,
            accessor,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TenantTemplateCatalogue>.Instance);

        var result = await catalogue.GetTemplateIdsAsync();

        Assert.Empty(result);
    }
}

public class UserAccessibleTemplateServiceTests
{
    private static readonly TemplateId TemplateA = new(Guid.NewGuid());
    private static readonly TemplateId TemplateB = new(Guid.NewGuid());
    private static readonly TemplateId TemplateC = new(Guid.NewGuid());

    [Fact]
    public async Task GetAccessibleTemplateIdsAsync_ShouldIntersectCatalogueWithPermissions()
    {
        var catalogue = Substitute.For<ITenantTemplateCatalogue>();
        catalogue.GetTemplateIdsAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { TemplateA, TemplateB }.AsReadOnly());

        var service = new UserAccessibleTemplateService(catalogue, CreatePermissionChecker(canManageTemplates: false));
        var permissions = new[]
        {
            CreatePermission(TemplateB),
            CreatePermission(TemplateC)
        };

        var result = await service.GetAccessibleTemplateIdsAsync(permissions);

        Assert.Single(result);
        Assert.Equal(TemplateB, result[0]);
    }

    [Fact]
    public async Task GetAccessibleTemplateIdsAsync_ShouldReturnFullCatalogue_WhenUserCanManageTemplates()
    {
        var catalogue = Substitute.For<ITenantTemplateCatalogue>();
        catalogue.GetTemplateIdsAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { TemplateA, TemplateB }.AsReadOnly());

        // SuperAdmin/Admin: no per-template permission rows (create apps via role bypass).
        var service = new UserAccessibleTemplateService(catalogue, CreatePermissionChecker(canManageTemplates: true));

        var result = await service.GetAccessibleTemplateIdsAsync(Array.Empty<Permission>());

        Assert.Equal(2, result.Count);
        Assert.Contains(TemplateA, result);
        Assert.Contains(TemplateB, result);
    }

    [Fact]
    public async Task GetAccessibleTemplateIdsAsync_ShouldReturnFullCatalogue_WhenUserHasTemplateAnyGrant()
    {
        var catalogue = Substitute.For<ITenantTemplateCatalogue>();
        catalogue.GetTemplateIdsAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { TemplateA, TemplateB }.AsReadOnly());

        var service = new UserAccessibleTemplateService(catalogue, CreatePermissionChecker(canManageTemplates: false));
        var permissions = new[]
        {
            new Permission(
                new PermissionId(Guid.NewGuid()),
                new UserId(Guid.NewGuid()),
                applicationId: null,
                PermissionConstants.AnyResourceKey,
                ResourceType.Template,
                AccessType.Write,
                DateTime.UtcNow,
                new UserId(Guid.NewGuid()))
        };

        var result = await service.GetAccessibleTemplateIdsAsync(permissions);

        Assert.Equal(2, result.Count);
        Assert.Contains(TemplateA, result);
        Assert.Contains(TemplateB, result);
    }

    [Fact]
    public async Task ResolveAccessibleListingFilterAsync_ShouldRestrictToRequestedAccessibleTemplate()
    {
        var catalogue = Substitute.For<ITenantTemplateCatalogue>();
        catalogue.GetTemplateIdsAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { TemplateA, TemplateB }.AsReadOnly());

        var service = new UserAccessibleTemplateService(catalogue, CreatePermissionChecker(canManageTemplates: false));
        var permissions = new[] { CreatePermission(TemplateA), CreatePermission(TemplateB) };

        var result = await service.ResolveAccessibleListingFilterAsync(permissions, TemplateA.Value);

        Assert.Single(result);
        Assert.Equal(TemplateA, result[0]);
    }

    private static IPermissionCheckerService CreatePermissionChecker(bool canManageTemplates)
    {
        var checker = Substitute.For<IPermissionCheckerService>();
        checker.CanManageTemplates().Returns(canManageTemplates);
        return checker;
    }

    private static Permission CreatePermission(TemplateId templateId) =>
        new(
            new PermissionId(Guid.NewGuid()),
            new UserId(Guid.NewGuid()),
            applicationId: null,
            templateId.Value.ToString(),
            ResourceType.Template,
            AccessType.Read,
            DateTime.UtcNow,
            new UserId(Guid.NewGuid()));
}
