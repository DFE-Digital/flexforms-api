using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using NSubstitute;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

/// <summary>Adds to <c>UserAccessibleTemplateServiceTests</c>: grants outside the tenant catalogue and bad keys never widen access.</summary>
public class UserAccessibleTemplateServiceTenantLimitTests
{
    private static readonly TemplateId InTenantA = new(Guid.Parse("a0000000-0000-4000-8000-000000000001"));
    private static readonly TemplateId InTenantB = new(Guid.Parse("a0000000-0000-4000-8000-000000000002"));
    private static readonly TemplateId OutsideTenant = new(Guid.Parse("a0000000-0000-4000-8000-000000000003"));
    private static readonly UserId Bob = new(Guid.NewGuid());

    private readonly ITenantTemplateCatalogue _catalogue = Substitute.For<ITenantTemplateCatalogue>();
    private readonly IPermissionCheckerService _permissions = Substitute.For<IPermissionCheckerService>();
    private readonly UserAccessibleTemplateService _service;

    public UserAccessibleTemplateServiceTenantLimitTests()
    {
        _catalogue.GetTemplateIdsAsync(Arg.Any<CancellationToken>()).Returns([InTenantA, InTenantB]);
        _service = new UserAccessibleTemplateService(_catalogue, _permissions);
    }

    [Fact]
    public async Task LowerCaseAnyGrant_GetsTheWholeTenantCatalogue()
    {
        var accessible = await _service.GetAccessibleTemplateIdsAsync([Grant(ResourceType.Template, "any", AccessType.Read)]);

        Assert.Equal([InTenantA, InTenantB], accessible);
    }

    [Fact]
    public async Task SpecificGrants_AreLimitedToTheTenantCatalogue()
    {
        var accessible = await _service.GetAccessibleTemplateIdsAsync(
        [
            Grant(ResourceType.Template, InTenantA.Value.ToString(), AccessType.Read),
            Grant(ResourceType.Template, OutsideTenant.Value.ToString(), AccessType.Manage),
            Grant(ResourceType.Template, Guid.Empty.ToString(), AccessType.Read),
            Grant(ResourceType.Template, "not-a-guid", AccessType.Read),
            Grant(ResourceType.Application, InTenantB.Value.ToString(), AccessType.Read),
        ]);

        Assert.Equal([InTenantA], accessible);
    }

    [Fact]
    public async Task EmptyCatalogue_GivesNothingEvenToAManager()
    {
        _catalogue.GetTemplateIdsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _permissions.CanManageTemplates().Returns(true);

        Assert.Empty(await _service.GetAccessibleTemplateIdsAsync([Grant(ResourceType.Template, "Any", AccessType.Read)]));
    }

    [Fact]
    public async Task ListingFilter_WithoutARequestedTemplateIsEverythingAccessible()
    {
        var filter = await _service.ResolveAccessibleListingFilterAsync(
            [Grant(ResourceType.Template, InTenantB.Value.ToString(), AccessType.Read)], requestedTemplateId: null);

        Assert.Equal([InTenantB], filter);
    }

    [Theory]
    [MemberData(nameof(NotAccessible))]
    public async Task ListingFilter_RequestedTemplateTheUserCannotSeeIsEmpty(Guid requested)
    {
        var filter = await _service.ResolveAccessibleListingFilterAsync(
            [Grant(ResourceType.Template, InTenantA.Value.ToString(), AccessType.Read)], requested);

        Assert.Empty(filter);
    }

    public static TheoryData<Guid> NotAccessible() => new(InTenantB.Value, OutsideTenant.Value);

    [Fact]
    public async Task ListingFilter_NothingAccessibleIsEmpty()
    {
        Assert.Empty(await _service.ResolveAccessibleListingFilterAsync([], InTenantA.Value));
    }

    private static Permission Grant(ResourceType type, string key, AccessType access) =>
        new(new PermissionId(Guid.NewGuid()), Bob, null, key, type, access, DateTime.UtcNow, Bob);
}
