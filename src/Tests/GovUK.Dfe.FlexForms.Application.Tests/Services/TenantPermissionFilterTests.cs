using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Enums;
using GovUK.Dfe.FlexForms.Application.Services;
using GovUK.Dfe.FlexForms.Domain.Common;
using GovUK.Dfe.FlexForms.Domain.Entities;
using GovUK.Dfe.FlexForms.Domain.ValueObjects;

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
    public void BelongsToTenant_ShouldKeepOnlyCurrentTenantOwnedApplicationPermissions()
    {
        var userId = new UserId(Guid.NewGuid());
        // Both templates appear in HostMappings/catalogue (overlap), but ownership differs.
        var tenantTemplateIds = new HashSet<Guid> { _tenantTemplateId, _otherTemplateId };
        var map = new Dictionary<Guid, TenantPermissionFilter.ApplicationOwnership>
        {
            [_tenantApplicationId] = new(_tenantTemplateId, _currentTenantId),
            [_otherApplicationId] = new(_otherTemplateId, _otherTenantId)
        };

        var tenantPermission = CreatePermission(userId, ResourceType.Application, _tenantApplicationId.ToString(), AccessType.Read);
        var otherPermission = CreatePermission(userId, ResourceType.Application, _otherApplicationId.ToString(), AccessType.Read);

        Assert.True(TenantPermissionFilter.BelongsToTenant(tenantPermission, _currentTenantId, tenantTemplateIds, map));
        Assert.False(TenantPermissionFilter.BelongsToTenant(otherPermission, _currentTenantId, tenantTemplateIds, map));
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
    public void IsTemplateInTenant_ShouldRejectOtherTenantOwnedTemplateEvenWhenInCatalogue()
    {
        var tenantTemplateIds = new HashSet<Guid> { _otherTemplateId };

        Assert.False(TenantPermissionFilter.IsTemplateInTenant(
            _otherTemplateId,
            _otherTenantId,
            _currentTenantId,
            tenantTemplateIds));
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
