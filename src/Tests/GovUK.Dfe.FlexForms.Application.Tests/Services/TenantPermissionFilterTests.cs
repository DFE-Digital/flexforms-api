using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Application.Tests.Helpers;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.Interfaces.Repositories;
using GovUK.Dfe.FlexForms.Domain.Services;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable;
using NSubstitute;
using ApplicationId = GovUK.Dfe.FlexForms.Domain.ValueObjects.ApplicationId;

namespace GovUK.Dfe.FlexForms.Application.Tests.Services;

public class TenantPermissionFilterTests
{
    private readonly Guid _currentTenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private readonly Guid _tenantTemplateId = Guid.NewGuid();
    private readonly Guid _otherTemplateId = Guid.NewGuid();
    private readonly Guid _tenantApplicationId = Guid.NewGuid();
    private readonly Guid _otherApplicationId = Guid.NewGuid();

    [Fact]
    public void BelongsToTenant_ShouldKeepTenantTemplatePermissions()
    {
        var userId = new UserId(Guid.NewGuid());
        var tenantTemplateIds = new HashSet<Guid> { _tenantTemplateId };
        var map = new Dictionary<Guid, TenantPermissionFilter.ApplicationOwnership>();

        var tenantPermission = CreatePermission(userId, ResourceType.Template, _tenantTemplateId.ToString(), AccessType.Read);
        var otherPermission = CreatePermission(userId, ResourceType.Template, _otherTemplateId.ToString(), AccessType.Read);

        Assert.True(TenantPermissionFilter.BelongsToTenant(tenantPermission, _currentTenantId, tenantTemplateIds, map));
        Assert.False(TenantPermissionFilter.BelongsToTenant(otherPermission, _currentTenantId, tenantTemplateIds, map));
    }

    [Fact]
    public void BelongsToTenant_ShouldKeepOnlyApplicationsWhoseTemplateIsInCatalogue()
    {
        var userId = new UserId(Guid.NewGuid());
        var tenantTemplateIds = new HashSet<Guid> { _tenantTemplateId };
        var map = new Dictionary<Guid, TenantPermissionFilter.ApplicationOwnership>
        {
            [_tenantApplicationId] = new(_tenantTemplateId),
            [_otherApplicationId] = new(_otherTemplateId)
        };

        var tenantPermission = CreatePermission(userId, ResourceType.Application, _tenantApplicationId.ToString(), AccessType.Read);
        var otherPermission = CreatePermission(userId, ResourceType.Application, _otherApplicationId.ToString(), AccessType.Read);

        Assert.True(TenantPermissionFilter.BelongsToTenant(tenantPermission, _currentTenantId, tenantTemplateIds, map));
        Assert.False(TenantPermissionFilter.BelongsToTenant(otherPermission, _currentTenantId, tenantTemplateIds, map));
    }

    [Fact]
    public void BelongsToTenant_ShouldKeepHostMappedApplication_WhenTemplateIsOwnedByAnotherTenant()
    {
        var userId = new UserId(Guid.NewGuid());
        var tenantTemplateIds = new HashSet<Guid> { _otherTemplateId };
        var map = new Dictionary<Guid, TenantPermissionFilter.ApplicationOwnership>
        {
            [_otherApplicationId] = new(_otherTemplateId)
        };

        var permission = CreatePermission(userId, ResourceType.Application, _otherApplicationId.ToString(), AccessType.Read);

        Assert.True(TenantPermissionFilter.BelongsToTenant(permission, _currentTenantId, tenantTemplateIds, map));
    }

    [Fact]
    public void BelongsToTenant_ShouldRejectUnknownApplicationPermissions()
    {
        var userId = new UserId(Guid.NewGuid());
        var tenantTemplateIds = new HashSet<Guid> { _tenantTemplateId };
        var unknownApplicationId = Guid.NewGuid();
        var permission = CreatePermission(userId, ResourceType.Application, unknownApplicationId.ToString(), AccessType.Read);

        Assert.False(TenantPermissionFilter.BelongsToTenant(
            permission,
            _currentTenantId,
            tenantTemplateIds,
            new Dictionary<Guid, TenantPermissionFilter.ApplicationOwnership>()));
    }

    [Fact]
    public void BelongsToTenant_ShouldKeepTenantWideAnyPermission()
    {
        var userId = new UserId(Guid.NewGuid());
        var tenantTemplateIds = new HashSet<Guid> { _tenantTemplateId };
        var permission = CreatePermission(userId, ResourceType.Application, PermissionConstants.AnyResourceKey, AccessType.Read);

        Assert.True(TenantPermissionFilter.BelongsToTenant(
            permission,
            _currentTenantId,
            tenantTemplateIds,
            new Dictionary<Guid, TenantPermissionFilter.ApplicationOwnership>()));
    }

    [Fact]
    public void BelongsToTenant_ShouldKeepOnlyCurrentTenantNotificationPermissions()
    {
        var userId = new UserId(Guid.NewGuid());
        var tenantTemplateIds = new HashSet<Guid> { _tenantTemplateId };
        var map = new Dictionary<Guid, TenantPermissionFilter.ApplicationOwnership>();

        var current = CreatePermission(
            userId,
            ResourceType.Notifications,
            TenantScopedIdentityKey.Combine(_currentTenantId, "user@example.com"),
            AccessType.Read);
        var other = CreatePermission(
            userId,
            ResourceType.Notifications,
            TenantScopedIdentityKey.Combine(_otherTenantId, "user@example.com"),
            AccessType.Read);
        var legacy = CreatePermission(
            userId,
            ResourceType.Notifications,
            "user@example.com",
            AccessType.Read);

            Assert.True(TenantPermissionFilter.BelongsToTenant(current, _currentTenantId, tenantTemplateIds, map));
            Assert.False(TenantPermissionFilter.BelongsToTenant(other, _currentTenantId, tenantTemplateIds, map));
            Assert.True(TenantPermissionFilter.BelongsToTenant(legacy, _currentTenantId, tenantTemplateIds, map));
    }

    [Fact]
    public async Task ApplicationBelongsToCurrentTenantAsync_ShouldAllowHostMappedTemplateOwnedByAnotherTenant()
    {
        var createdBy = new UserId(Guid.NewGuid());
        var application = new Domain.Entities.Application(
            new ApplicationId(_tenantApplicationId),
            "VST-1",
            new TemplateVersionId(Guid.NewGuid()),
            DateTime.UtcNow,
            createdBy);
        ApplicationListingTestHelper.AttachTemplateVersion(application, new TemplateId(_otherTemplateId), createdBy);

        var filter = CreateFilter(
            catalogueTemplateIds: [new TemplateId(_otherTemplateId)],
            applications: [application]);

        Assert.True(await filter.ApplicationBelongsToCurrentTenantAsync(_tenantApplicationId));
    }

    [Fact]
    public async Task ApplicationBelongsToCurrentTenantAsync_ShouldDeny_WhenTemplateIsNotInCatalogue()
    {
        var createdBy = new UserId(Guid.NewGuid());
        var application = new Domain.Entities.Application(
            new ApplicationId(_tenantApplicationId),
            "VST-1",
            new TemplateVersionId(Guid.NewGuid()),
            DateTime.UtcNow,
            createdBy);
        ApplicationListingTestHelper.AttachTemplateVersion(application, new TemplateId(_otherTemplateId), createdBy);

        var filter = CreateFilter(
            catalogueTemplateIds: [new TemplateId(_tenantTemplateId)],
            applications: [application]);

        Assert.False(await filter.ApplicationBelongsToCurrentTenantAsync(_tenantApplicationId));
    }

    private TenantPermissionFilter CreateFilter(
        IReadOnlyList<TemplateId> catalogueTemplateIds,
        IReadOnlyList<Domain.Entities.Application> applications)
    {
        var catalogue = Substitute.For<ITenantTemplateCatalogue>();
        catalogue.GetTemplateIdsAsync(Arg.Any<CancellationToken>())
            .Returns(catalogueTemplateIds.ToList().AsReadOnly());

        var appRepo = Substitute.For<IApplicationRepository>();
        appRepo.Query().Returns(applications.AsQueryable().BuildMock());

        var settings = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var accessor = Substitute.For<ITenantContextAccessor>();
        accessor.CurrentTenant.Returns(new TenantConfiguration(_currentTenantId, "Visits2", settings, []));

        return new TenantPermissionFilter(
            catalogue,
            appRepo,
            accessor,
            NullLogger<TenantPermissionFilter>.Instance);
    }

    private static Permission CreatePermission(
        UserId userId,
        ResourceType resourceType,
        string resourceKey,
        AccessType accessType) =>
        new(
            new PermissionId(Guid.NewGuid()),
            userId,
            null,
            resourceKey,
            resourceType,
            accessType,
            DateTime.UtcNow,
            userId);
}
